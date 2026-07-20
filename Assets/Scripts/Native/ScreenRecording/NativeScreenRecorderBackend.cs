using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public sealed class NativeScreenRecorderBackend : IScreenRecorderBackend
{
    private const string PluginName = "MajdataScreenRecorder";

    public bool IsInitialized { get; private set; }
    public bool UsesNativeTextureInput => true;
    public string BackendName => "Native D3D11 hardware encoder";

    public void Initialize(ScreenRecorderBackendConfig config)
    {
        var nativeConfig = new NativeRecorderConfig
        {
            outputPath = config.OutputPath,
            width = config.Width,
            height = config.Height,
            framesPerSecond = config.FramesPerSecond,
            sampleRate = config.SampleRate,
            channels = config.Channels,
            unityTextureFormat = (int)config.UnityTextureFormat
        };

        if (!SR_Initialize(ref nativeConfig))
            throw new InvalidOperationException(GetLastError());

        IsInitialized = true;
    }

    public void SubmitTextureFrame(IntPtr nativeTexture, long pts)
    {
        if (!SR_SubmitFrame(nativeTexture, pts))
            throw new InvalidOperationException(GetLastError());
    }

    public void QueueCpuVideoFrame(NativeArray<byte> rgbaData, ManualResetEventSlim completion) =>
        throw new NotSupportedException("The native backend accepts Unity native texture pointers, not CPU RGBA frames.");

    public unsafe void WriteAudioSamples(NativeArray<float> samples, int sourceOffset, int sampleCount)
    {
        if (sampleCount <= 0)
            return;

        var ptr = (float*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(samples) + sourceOffset;
        if (!SR_WriteAudioSamples(ptr, sampleCount))
            throw new InvalidOperationException(GetLastError());
    }

    public void ThrowIfFailed()
    {
        if (!SR_IsHealthy())
            throw new InvalidOperationException(GetLastError());
    }

    public void Dispose()
    {
        if (!IsInitialized)
            return;

        try
        {
            if (!SR_Stop())
                throw new InvalidOperationException(GetLastError());
        }
        finally
        {
            IsInitialized = false;
        }
    }

    private static string GetLastError()
    {
        var message = Marshal.PtrToStringAnsi(SR_GetLastError());
        return string.IsNullOrEmpty(message) ? "Native screen recorder failed." : message;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct NativeRecorderConfig
    {
        [MarshalAs(UnmanagedType.LPStr)] public string outputPath;
        public int width;
        public int height;
        public int framesPerSecond;
        public int sampleRate;
        public int channels;
        public int unityTextureFormat;
    }

    [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SR_Initialize(ref NativeRecorderConfig config);

    [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SR_SubmitFrame(IntPtr nativeTexture, long pts);

    [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe bool SR_WriteAudioSamples(float* samples, int sampleCount);

    [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SR_Stop();

    [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SR_IsHealthy();

    [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SR_GetLastError();
}
