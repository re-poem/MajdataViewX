using System;
using UnityEngine;

public static class ScreenRecorderBackendFactory
{
    public static IScreenRecorderBackend CreatePreferredBackend(ScreenRecorderBackendConfig config)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
        {
            var nativeBackend = new NativeScreenRecorderBackend();
            try
            {
                nativeBackend.Initialize(config);
                Debug.Log($"[ScreenRecorder] Using {nativeBackend.BackendName}.");
                return nativeBackend;
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is InvalidOperationException)
            {
                Debug.LogWarning($"[ScreenRecorder] Hardware recorder unavailable: {exception.Message}. Falling back to CPU FFmpeg.");
                nativeBackend.Dispose();
            }
        }
#endif

        var fallback = new LegacyFFmpegScreenRecorderBackend();
        fallback.Initialize(config);
        Debug.Log($"[ScreenRecorder] Using {fallback.BackendName}.");
        return fallback;
    }
}
