using System;
using System.Threading;
using Unity.Collections;

public sealed class LegacyFFmpegScreenRecorderBackend : IScreenRecorderBackend
{
    public bool IsInitialized => FFmpegMediaEncoder.IsInitialized;
    public bool UsesNativeTextureInput => false;
    public string BackendName => "FFmpeg CPU fallback";

    public void Initialize(ScreenRecorderBackendConfig config)
    {
        FFmpegMediaEncoder.Initialize(
            config.OutputPath,
            config.Width,
            config.Height,
            config.FramesPerSecond,
            config.SampleRate,
            config.Channels);
    }

    public void SubmitTextureFrame(IntPtr nativeTexture, long pts) =>
        throw new NotSupportedException("The legacy FFmpeg backend requires CPU RGBA frames.");

    public void QueueCpuVideoFrame(NativeArray<byte> rgbaData, ManualResetEventSlim completion) =>
        FFmpegMediaEncoder.QueueVideoFrame(rgbaData, completion);

    public void WriteAudioSamples(NativeArray<float> samples, int sourceOffset, int sampleCount) =>
        FFmpegMediaEncoder.WriteAudioSamples(samples, sourceOffset, sampleCount);

    public void ThrowIfFailed() => FFmpegMediaEncoder.ThrowIfEncodingFailed();

    public void Dispose() => FFmpegMediaEncoder.Dispose();
}
