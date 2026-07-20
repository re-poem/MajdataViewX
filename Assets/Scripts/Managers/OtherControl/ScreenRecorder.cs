#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using JetBrains.Annotations;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

using static MajCtx;

#endregion

public class ScreenRecorder : MonoBehaviour
{
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

    // Triple buffering overlaps rendering, GPU readback and CPU encoding while
    // keeping the number of in-flight requests bounded across graphics APIs.
    private const int ReadbackSlotCount = 3;

    private sealed class VideoReadbackSlot
    {
        public readonly RenderTexture Target;
        public readonly ManualResetEventSlim EncodingCompleted = new(true);
        public NativeArray<byte> Buffer;
        public AsyncGPUReadbackRequest Request;

        public VideoReadbackSlot(int width, int height)
        {
            Target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            try
            {
                if (!Target.Create())
                    throw new InvalidOperationException("Could not create a video readback render target.");

                Buffer = new NativeArray<byte>(
                    checked(width * height * 4),
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }
            catch
            {
                if (Target.IsCreated()) Target.Release();
                Destroy(Target);
                throw;
            }
        }
    }

    Text errText;

    public bool IsRecording { get; private set; }

    private void Awake()
    {
        _screenRecorder = this;
    }

    private void Start()
    {
        errText = GameObject.Find("ErrText").GetComponent<Text>();
    }

    public async UniTask StartRecording(string maidataPath,
        int fps, bool resizeBg, [CanBeNull] Action onStart = null)
    {
        QualitySettings.vSyncCount = 0;
        await CaptureScreen(maidataPath, fps, resizeBg, onStart);
        QualitySettings.vSyncCount = 1;
    }

    public void StopRecording()
    {
        IsRecording = false;
    }

    public void ResetState()
    {
        StopRecording();
        errText.text = string.Empty;
    }

    private async UniTask CaptureScreen(string maidataPath,
        int fps, bool resizeBg, [CanBeNull] Action onStart = null)
    {
        if (fps <= 0)
        {
            errText.text = "Output frame rate must be greater than zero.";
            return;
        }

        if (Screen.width % 2 != 0 || Screen.height % 2 != 0)
        {
            errText.text = $"无法渲染：分辨率 {Screen.width}x{Screen.height} 不是偶数。";
            return;
        }

        const string finalName = "out.mp4";

        IsRecording = true;
        var width = Screen.width;
        var height = Screen.height;
        var frameDuration = 1.0 / fps;
        var recordingElapsedTime = 0.0;
        long videoFramePts = 0;
        var outputSucceeded = false;
        Queue<VideoReadbackSlot> pendingReadbacks = null;
        Queue<VideoReadbackSlot> pendingEncodes = null;
        Stack<VideoReadbackSlot> availableSlots = null;
        IScreenRecorderBackend recorderBackend = null;
        RenderTexture nativeCaptureTarget = null;

        try
        {
            var outPath = Path.Combine(maidataPath, finalName);
            if (File.Exists(outPath)) File.Delete(outPath);

            var backendConfig = new ScreenRecorderBackendConfig(
                outPath,
                width,
                height,
                fps,
                _audioManager.SampleRate,
                _audioManager.Channels,
                RenderTextureFormat.ARGB32);
            recorderBackend = ScreenRecorderBackendFactory.CreatePreferredBackend(backendConfig);
            ScreenRecorderBackendHub.SetCurrent(recorderBackend);

            if (recorderBackend.UsesNativeTextureInput)
            {
                nativeCaptureTarget = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                if (!nativeCaptureTarget.Create())
                    throw new InvalidOperationException("Could not create the native video capture render target.");
            }
            else
            {
                pendingReadbacks = new Queue<VideoReadbackSlot>(ReadbackSlotCount);
                pendingEncodes = new Queue<VideoReadbackSlot>(ReadbackSlotCount);
                availableSlots = new Stack<VideoReadbackSlot>(ReadbackSlotCount);
                for (var i = 0; i < ReadbackSlotCount; i++)
                    availableSlots.Push(new VideoReadbackSlot(width, height));
            }

            onStart?.Invoke();
            _audioManager.BeginRecordingAudio(_timeProvider.AudioTime, _timeProvider.CurrentSpeed);

            while (IsRecording)
            {
                await UniTask.WaitForEndOfFrame(this);
                var frameEndTime = recordingElapsedTime + frameDuration;
                _audioManager.UpdateRecordingAudioFrame(recordingElapsedTime, frameEndTime);

                recorderBackend.ThrowIfFailed();

                if (recorderBackend.UsesNativeTextureInput)
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(nativeCaptureTarget);
                    recorderBackend.SubmitTextureFrame(
                        nativeCaptureTarget.GetNativeTexturePtr(),
                        videoFramePts++);
                }
                else
                {
                    DrainCompletedVideoEncodes(recorderBackend, pendingEncodes, availableSlots);
                    DrainReadyVideoFrames(
                        recorderBackend,
                        pendingReadbacks,
                        pendingEncodes,
                        availableSlots);

                    // Apply backpressure before reusing memory still owned by the
                    // GPU or the encoding worker.
                    if (availableSlots.Count == 0)
                    {
                        if (pendingReadbacks.Count > 0)
                        {
                            QueueOldestVideoFrame(
                                recorderBackend,
                                pendingReadbacks,
                                pendingEncodes,
                                availableSlots,
                                true);
                        }

                        ReclaimOldestEncodedVideoFrame(
                            recorderBackend,
                            pendingEncodes,
                            availableSlots,
                            true);
                    }

                    var slot = availableSlots.Pop();
                    try
                    {
                        ScreenCapture.CaptureScreenshotIntoRenderTexture(slot.Target);
                        slot.Request = AsyncGPUReadback.RequestIntoNativeArray(
                            ref slot.Buffer,
                            slot.Target,
                            0,
                            TextureFormat.RGBA32,
                            null);
                        pendingReadbacks.Enqueue(slot);
                    }
                    catch
                    {
                        availableSlots.Push(slot);
                        throw;
                    }
                }

                recordingElapsedTime = frameEndTime;
            }

            _audioManager.EndRecordingAudio((float)recordingElapsedTime);

            while (pendingReadbacks != null && pendingReadbacks.Count > 0)
            {
                QueueOldestVideoFrame(
                    recorderBackend,
                    pendingReadbacks,
                    pendingEncodes,
                    availableSlots,
                    true);
            }
            while (pendingEncodes != null && pendingEncodes.Count > 0)
            {
                ReclaimOldestEncodedVideoFrame(
                    recorderBackend,
                    pendingEncodes,
                    availableSlots,
                    true);
            }

            recorderBackend.Dispose();
            ScreenRecorderBackendHub.ClearCurrent(recorderBackend);
            recorderBackend = null;
            outputSucceeded = true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            errText.text = $"Encoding failed: {e.Message}";
        }
        finally
        {
            IsRecording = false;
            try
            {
                if (recorderBackend != null)
                {
                    recorderBackend.Dispose();
                    ScreenRecorderBackendHub.ClearCurrent(recorderBackend);
                    recorderBackend = null;
                }
            }
            catch (Exception ex)
            {
                outputSucceeded = false;
                Debug.LogException(ex);
                errText.text = $"Finalizing the recording failed: {ex.Message}";
            }

            // 剩下的

            // Do not release render targets or NativeArrays while the GPU owns them.
            while (pendingReadbacks != null && pendingReadbacks.Count > 0)
            {
                var slot = pendingReadbacks.Dequeue();
                if (!slot.Request.done) slot.Request.WaitForCompletion();
                availableSlots.Push(slot);
            }
            while (pendingEncodes != null && pendingEncodes.Count > 0)
            {
                var slot = pendingEncodes.Dequeue();
                slot.EncodingCompleted.Wait();
                availableSlots.Push(slot);
            }
            while (availableSlots != null && availableSlots.Count > 0)
            {
                var slot = availableSlots.Pop();
                if (slot.Buffer.IsCreated) slot.Buffer.Dispose();
                slot.EncodingCompleted.Dispose();
                slot.Target.Release();
                Destroy(slot.Target);
            }

            if (nativeCaptureTarget != null)
            {
                nativeCaptureTarget.Release();
                Destroy(nativeCaptureTarget);
            }

            _audioManager.ReleaseRecordingAudio();
            var resultPath = Path.Combine(maidataPath, finalName);
            if (outputSucceeded && File.Exists(resultPath))
                OpenFileLocation(resultPath);

            RenderTexture.active = null;
        }
    }

    private static void DrainReadyVideoFrames(
        IScreenRecorderBackend recorderBackend,
        Queue<VideoReadbackSlot> requests,
        Queue<VideoReadbackSlot> pendingEncodes,
        Stack<VideoReadbackSlot> availableSlots)
    {
        while (QueueOldestVideoFrame(
                   recorderBackend,
                   requests,
                   pendingEncodes,
                   availableSlots,
                   false))
        {
        }
    }

    private static bool QueueOldestVideoFrame(
        IScreenRecorderBackend recorderBackend,
        Queue<VideoReadbackSlot> requests,
        Queue<VideoReadbackSlot> pendingEncodes,
        Stack<VideoReadbackSlot> availableSlots,
        bool waitForCompletion)
    {
        if (requests.Count == 0)
            return false;

        var slot = requests.Peek();
        if (!slot.Request.done)
        {
            if (!waitForCompletion)
                return false;
            slot.Request.WaitForCompletion();
        }

        requests.Dequeue();
        try
        {
            if (slot.Request.hasError)
                throw new InvalidOperationException(
                    "Async GPU readback failed while recording a video frame.");

            recorderBackend.QueueCpuVideoFrame(
                slot.Buffer,
                slot.EncodingCompleted);
            pendingEncodes.Enqueue(slot);
            return true;
        }
        catch
        {
            availableSlots.Push(slot);
            throw;
        }
    }

    private static void DrainCompletedVideoEncodes(
        IScreenRecorderBackend recorderBackend,
        Queue<VideoReadbackSlot> pendingEncodes,
        Stack<VideoReadbackSlot> availableSlots)
    {
        while (ReclaimOldestEncodedVideoFrame(
                   recorderBackend,
                   pendingEncodes,
                   availableSlots,
                   false))
        {
        }
    }

    private static bool ReclaimOldestEncodedVideoFrame(
        IScreenRecorderBackend recorderBackend,
        Queue<VideoReadbackSlot> pendingEncodes,
        Stack<VideoReadbackSlot> availableSlots,
        bool waitForCompletion)
    {
        if (pendingEncodes.Count == 0)
            return false;

        var slot = pendingEncodes.Peek();
        if (!slot.EncodingCompleted.IsSet)
        {
            if (!waitForCompletion)
                return false;
            slot.EncodingCompleted.Wait();
        }

        pendingEncodes.Dequeue();
        availableSlots.Push(slot);
        recorderBackend.ThrowIfFailed();
        return true;
    }

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
