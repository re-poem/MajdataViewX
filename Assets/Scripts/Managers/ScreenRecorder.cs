using Cysharp.Threading.Tasks;
using JetBrains.Annotations;
using MajdataViewX.Types.Rendering;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;
using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class ScreenRecorder : MonoBehaviour
    {
        private const string EncoderDllName = "RenderingOut";

        [DllImport(EncoderDllName, CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr video_encoder_create(
            int quality,
            int width,
            int height,
            int fps,
            [MarshalAs(UnmanagedType.LPStr)] string filename);

        [DllImport(EncoderDllName, CallingConvention = CallingConvention.StdCall)]
        private static extern int video_encoder_submit_frame(
            IntPtr encoder,
            IntPtr nativeTexture);

        [DllImport(EncoderDllName, CallingConvention = CallingConvention.StdCall)]
        private static extern int video_encoder_mux_audio(
            IntPtr encoder,
            IntPtr pcmData,
            int pcmLengthBytes,
            int sampleRate,
            int channels);

        [DllImport(EncoderDllName, CallingConvention = CallingConvention.StdCall)]
        private static extern void video_encoder_free(IntPtr encoder);


        public bool IsRecording { get; private set; }
        public static bool IsSupported
        {
            get
            {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return true;
#else
                return false;
#endif
            }
        }

        private void Awake()
        {
            _screenRecorder = this;
        }


        public async UniTask StartRecording(string maidataPath,
            int fps, ExportQuality quality, [CanBeNull] Action onStart = null)
        {
            if (!IsSupported)
            {
                _wsServer.Error("Video recording is currently supported only on Windows.");
                return;
            }

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

            const string finalName = "out.mp4";

            IsRecording = true;
            // H.264 (NV12) requires even dimensions
            // crop odd pixels so encoding always succeeds.
            var width = Screen.width & ~1;
            var height = Screen.height & ~1;
            var frameDuration = 1.0 / fps;
            var recordingElapsedTime = 0.0;
            var outputSucceeded = false;
            var captureTexture = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.BGRA32)
            {
                name = "Screen Recorder Capture",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            var encoder = IntPtr.Zero;

            try
            {
                if (!captureTexture.Create())
                    throw new InvalidOperationException(
                        "Could not create the screen recorder render target.");

                var outPath = Path.Combine(maidataPath, finalName);
                if (File.Exists(outPath)) File.Delete(outPath);

                encoder = video_encoder_create(
                    (int)quality,
                    width,
                    height,
                    fps,
                    outPath);
                if (encoder == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "RenderingOut could not create the video encoder.");

                onStart?.Invoke();
                _audioManager.BeginRecordingAudio(_timeProvider.AudioTime, _timeProvider.CurrentSpeed);

                while (IsRecording)
                {
                    await UniTask.WaitForEndOfFrame(this);
                    var frameEndTime = recordingElapsedTime + frameDuration;
                    _audioManager.UpdateRecordingAudioFrame(recordingElapsedTime, frameEndTime);

                    ScreenCapture.CaptureScreenshotIntoRenderTexture(captureTexture);
                    var nativeTexture = captureTexture.GetNativeTexturePtr();
                    if (nativeTexture == IntPtr.Zero)
                        throw new InvalidOperationException(
                            "The screen recorder render target has no native texture.");

                    var submitResult = video_encoder_submit_frame(encoder, nativeTexture);
                    if (submitResult < 0)
                        throw new InvalidOperationException(
                            $"RenderingOut failed to encode a video frame ({submitResult}).");

                    recordingElapsedTime = frameEndTime;
                }

                _audioManager.EndRecordingAudio((float)recordingElapsedTime);
                MuxRecordingAudio(encoder);
                FreeEncoder(ref encoder);
                outputSucceeded = true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _wsServer.Error(e);
            }
            finally
            {
                IsRecording = false;
                try
                {
                    FreeEncoder(ref encoder);
                }
                catch (Exception ex)
                {
                    outputSucceeded = false;
                    Debug.LogException(ex);
                    _wsServer.Error(ex);
                }

                _audioManager.ReleaseRecordingAudio();
                var resultPath = Path.Combine(maidataPath, finalName);
                if (outputSucceeded && File.Exists(resultPath))
                    OpenFileLocation(resultPath);

                RenderTexture.active = null;
                captureTexture.Release();
                Destroy(captureTexture);
            }
        }

        private static unsafe void MuxRecordingAudio(IntPtr encoder)
        {
            var pcmData = _audioManager.GetRecordingBuffer(out var sampleCount);
            if (sampleCount == 0)
                return;

            var pcmDataPointer = (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(pcmData);
            var muxResult = video_encoder_mux_audio(
                encoder,
                pcmDataPointer,
                checked(sampleCount * sizeof(float)),
                AudioManager.SAMPLERATE,
                AudioManager.CHANNELS);
            if (muxResult < 0)
                throw new InvalidOperationException(
                    $"RenderingOut failed to mux the recorded audio ({muxResult}).");
        }

        private static void FreeEncoder(ref IntPtr encoder)
        {
            if (encoder == IntPtr.Zero)
                return;

            var encoderToFree = encoder;
            encoder = IntPtr.Zero;
            video_encoder_free(encoderToFree);
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
