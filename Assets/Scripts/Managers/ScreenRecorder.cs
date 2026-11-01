using Cysharp.Threading.Tasks;
using JetBrains.Annotations;
using MajdataPlay.FFmpeg;
using MajdataViewX.Types.Rendering;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    /// <summary>
    /// 导出录像。画面由同一 GameObject 上的 <see cref="FFmpegCameraCapturer"/> 采集，
    /// 音轨由 <see cref="AudioManager"/> 离线混音，停止后经 <see cref="FFmpegMediaMuxer"/> 封装进最终 MP4。
    /// </summary>
    /// <remarks>
    /// 视频先写入同目录的临时文件：capturer 只录视频，封装需要已收尾的容器，
    /// 且 muxer 复制视频包、不重新编码，所以音频只能在停止之后并入。
    /// </remarks>
    public class ScreenRecorder : MonoBehaviour
    {
        /// <summary>最终输出文件名，位于谱面目录。</summary>
        private const string FinalName = "out.mp4";
        /// <summary>仅含视频的中间文件名，封装成功后删除。</summary>
        private const string TempVideoName = "out.video.mp4";
        /// <summary>目标码率下限，避免小窗口导出码率过低。</summary>
        private const long MinimumBitRate = 1_000_000;

        private FFmpegCameraCapturer _capturer;
        /// <summary>保留场景里配置的编码器偏好，只覆盖尺寸、帧率与码率。</summary>
        private EncoderOptions _baseOptions;

        public bool IsRecording { get; private set; }

        private void Awake()
        {
            _screenRecorder = this;
        }

        private void Start()
        {
            _capturer = GetComponent<FFmpegCameraCapturer>();
            if (_capturer == null)
            {
                _wsServer.Error("ScreenRecorder requires an FFmpegCameraCapturer on the same GameObject.");
                return;
            }

            // Options 每次录制都会被替换成新对象，这里持有的是场景序列化的那份配置。
            _baseOptions = _capturer.Options;
        }

        public async UniTask StartRecording(string maidataPath,
            int fps, ExportQuality quality, [CanBeNull] Action onStart = null)
        {
            QualitySettings.vSyncCount = 0;
            try
            {
                await CaptureScreen(maidataPath, fps, quality, onStart);
            }
            finally
            {
                QualitySettings.vSyncCount = 1;
            }
        }

        public void StopRecording()
        {
            IsRecording = false;
        }

        private async UniTask CaptureScreen(string maidataPath,
            int fps, ExportQuality quality, [CanBeNull] Action onStart = null)
        {
            if (fps <= 0)
            {
                _wsServer.Error("Encoding cannot start: Output frame rate must be greater than zero.");
                return;
            }

            if (_capturer == null)
            {
                _wsServer.Error("Encoding cannot start: no FFmpegCameraCapturer is attached.");
                return;
            }

            // H.264 (NV12) requires even dimensions
            // crop odd pixels so encoding always succeeds.
            var width = Screen.width & ~1;
            var height = Screen.height & ~1;
            if (width < 2 || height < 2)
            {
                _wsServer.Error($"Encoding cannot start: {Screen.width}x{Screen.height} is too small to crop.");
                return;
            }

            var finalPath = Path.GetFullPath(Path.Combine(maidataPath, FinalName));
            var tempVideoPath = Path.GetFullPath(Path.Combine(maidataPath, TempVideoName));
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? maidataPath);

            var frameDuration = 1.0 / fps;
            var recordingElapsedTime = 0.0;
            var audioStarted = false;
            var capturerStopped = false;
            var outputSucceeded = false;

            _capturer.Options = CreateOptions(width, height, fps, quality);

            // 音轨的 0 号采样与视频第 0 帧是同一时刻：两者都在录制真正开始时启动。
            void OnStarted(FFmpegCameraCapturer capturer)
            {
                onStart?.Invoke();
                _audioManager.BeginRecordingAudio(_timeProvider.AudioTime, _timeProvider.CurrentSpeed);
                audioStarted = true;
            }

            IsRecording = true;
            _capturer.Started += OnStarted;
            try
            {
                File.Delete(finalPath);
                File.Delete(tempVideoPath);
                await _capturer.StartRecordingAsync(tempVideoPath);
                if (!audioStarted)
                {
                    throw new InvalidOperationException("Recording started without its audio buffer.");
                }

                while (IsRecording)
                {
                    await UniTask.WaitForEndOfFrame(this);
                    // 录制暂停时视频时钟冻结，计时与音频一起停住，避免音画越走越偏。
                    if (_capturer.IsPaused)
                    {
                        continue;
                    }

                    var frameEndTime = recordingElapsedTime + frameDuration;
                    _audioManager.UpdateRecordingAudioFrame(recordingElapsedTime, frameEndTime);
                    recordingElapsedTime = frameEndTime;
                }

                IsRecording = false;
                _audioManager.EndRecordingAudio((float)recordingElapsedTime);
                await _capturer.StopRecordingAsync();
                capturerStopped = true;
                outputSucceeded = await MuxRecordingAudio(tempVideoPath, finalPath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _wsServer.Error(e);
            }
            finally
            {
                IsRecording = false;
                _capturer.Started -= OnStarted;
                if (!capturerStopped)
                {
                    await StopCapturerQuietlyAsync();
                }

                _audioManager.ReleaseRecordingAudio();
                if (!outputSucceeded && File.Exists(tempVideoPath))
                {
                    // 封装或录制失败时也留下可用产物，不丢弃已经编码的画面。
                    try
                    {
                        MoveReplace(tempVideoPath, finalPath);
                        outputSucceeded = true;
                    }
                    catch (Exception ex)
                    {
                        _wsServer.Error(ex);
                    }
                }

                if (outputSucceeded && File.Exists(finalPath))
                {
                    OpenFileLocation(finalPath);
                }
            }
        }

        /// <summary>把录好的音频混入已收尾的视频，失败时保留纯视频。</summary>
        /// <param name="videoPath">capturer 写出的临时视频文件。</param>
        /// <param name="finalPath">最终输出路径。</param>
        /// <returns>True 当最终文件已经生成（带音轨或退化为纯视频）。</returns>
        private async UniTask<bool> MuxRecordingAudio(string videoPath, string finalPath)
        {
            var pcmData = _audioManager.GetRecordingBuffer(out var sampleCount);
            if (sampleCount <= 0)
            {
                _wsServer.Error("The recording audio buffer is empty; writing a video-only file.");
                MoveReplace(videoPath, finalPath);
                return true;
            }

            try
            {
                var pcmByteCount = checked(sampleCount * sizeof(float));
                UnmanagedMemoryStream pcm;
                unsafe
                {
                    var pcmPointer = (byte*)pcmData.GetUnsafeReadOnlyPtr();
                    pcm = new UnmanagedMemoryStream(pcmPointer, pcmByteCount);
                }

                await using (pcm)
                await using (var output = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16))
                {
                    // 音频缓冲从录制起点开始，音轨原点与视频原点一致，不需要额外延迟。
                    await UniTask.RunOnThreadPool(() => FFmpegMediaMuxer.Mux(
                        videoPath,
                        pcm,
                        AudioManager.SAMPLE_RATE,
                        AudioManager.CHANNELS,
                        output,
                        MediaContainerFormat.MP4,
                        TimeSpan.Zero));
                }

                File.Delete(videoPath);
                return true;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Debug.LogException(e);
                _wsServer.Error(e);
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                MoveReplace(videoPath, finalPath);
                return true;
            }
        }

        /// <summary>把已完成的文件覆盖写入目标路径。</summary>
        /// <param name="source">源文件，移动后删除。</param>
        /// <param name="destination">目标路径，存在时被覆盖。</param>
        private static void MoveReplace(string source, string destination)
        {
            File.Copy(source, destination, true);
            File.Delete(source);
        }

        /// <summary>停止 capturer 并吞掉收尾异常，供异常路径使用。</summary>
        private async UniTask StopCapturerQuietlyAsync()
        {
            try
            {
                await _capturer.StopRecordingAsync();
            }
            catch (Exception e)
            {
                _wsServer.Error(e);
            }
        }

        
        
        /// <summary>把导出质量换算成编码选项，沿用场景里配置的后端、格式与线程数。</summary>
        private EncoderOptions CreateOptions(int width, int height, int fps, ExportQuality quality)
        {
            var bitsPerPixel = quality switch
            {
                ExportQuality.Low => 0.06,
                ExportQuality.Medium => 0.10,
                ExportQuality.High => 0.16,
                ExportQuality.Ultra => 0.24,
                _ => 0.16
            };
            // 目标码率按每像素每帧位数换算并取整到 kBit/s；VBR 上限留一倍余量给复杂画面。
            var bitRate = Math.Max(
                (long)(width * (double)height * fps * bitsPerPixel / 1000) * 1000,
                MinimumBitRate);
            return new EncoderOptions
            {
                Width = width,
                Height = height,
                FrameRate = fps,
                PreferredEncoderType = _baseOptions.PreferredEncoderType,
                Format = _baseOptions.Format,
                MaximumSoftwareThreads = Math.Min(Environment.ProcessorCount, 8),
                BitRate = bitRate,
                MaximumBitRate = bitRate * 2,
                RateControlMode = VideoRateControlMode.VBR
            };
        }

        
        
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ShellExecuteW(
            IntPtr window,
            string operation,
            string file,
            string parameters,
            string directory,
            int showCommand);
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
    [DllImport("libSystem.dylib", EntryPoint = "system")]
    private static extern int RunSystemCommand(string command);
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
    [DllImport("libc", EntryPoint = "system")]
    private static extern int RunSystemCommand(string command);
#endif
        private static void OpenFileLocation(string filePath)
        {
            var fullPath = Path.GetFullPath(filePath);

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var result = ShellExecuteW(
                IntPtr.Zero,
                "open",
                "explorer.exe",
                $"/select,\"{fullPath}\"",
                string.Empty,
                1);
            if (result.ToInt64() > 32) return;
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        if (RunSystemCommand($"open -R {QuoteShellArgument(fullPath)}") == 0) return;
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        var linuxDirectory = Path.GetDirectoryName(fullPath);
        if (linuxDirectory is not null &&
            RunSystemCommand($"xdg-open {QuoteShellArgument(linuxDirectory)} >/dev/null 2>&1 &") == 0)
            return;
#endif

            var directoryPath = Path.GetDirectoryName(fullPath);
            if (directoryPath is not null)
                Application.OpenURL(new Uri(directoryPath + Path.DirectorySeparatorChar).AbsoluteUri);
        }

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
    private static string QuoteShellArgument(string value) =>
        $"'{value.Replace("'", "'\"'\"'")}'";
#endif
    }
}
