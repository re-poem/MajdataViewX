using System;
using System.Threading;
using Unity.Collections;
using UnityEngine;

public interface IScreenRecorderBackend : IDisposable
{
    bool IsInitialized { get; }
    bool UsesNativeTextureInput { get; }
    string BackendName { get; }

    void Initialize(ScreenRecorderBackendConfig config);
    void SubmitTextureFrame(IntPtr nativeTexture, long pts);
    void QueueCpuVideoFrame(NativeArray<byte> rgbaData, ManualResetEventSlim completion);
    void WriteAudioSamples(NativeArray<float> samples, int sourceOffset, int sampleCount);
    void ThrowIfFailed();
}

public readonly struct ScreenRecorderBackendConfig
{
    public readonly string OutputPath;
    public readonly int Width;
    public readonly int Height;
    public readonly int FramesPerSecond;
    public readonly int SampleRate;
    public readonly int Channels;
    public readonly RenderTextureFormat UnityTextureFormat;

    public ScreenRecorderBackendConfig(
        string outputPath,
        int width,
        int height,
        int framesPerSecond,
        int sampleRate,
        int channels,
        RenderTextureFormat unityTextureFormat)
    {
        OutputPath = outputPath;
        Width = width;
        Height = height;
        FramesPerSecond = framesPerSecond;
        SampleRate = sampleRate;
        Channels = channels;
        UnityTextureFormat = unityTextureFormat;
    }
}
