using Unity.Collections;

public static class ScreenRecorderBackendHub
{
    public static IScreenRecorderBackend Current { get; private set; }

    public static void SetCurrent(IScreenRecorderBackend backend) => Current = backend;

    public static void ClearCurrent(IScreenRecorderBackend backend)
    {
        if (ReferenceEquals(Current, backend))
            Current = null;
    }

    public static void WriteAudioSamples(NativeArray<float> samples, int sourceOffset, int sampleCount)
    {
        var backend = Current;
        if (backend == null || !backend.IsInitialized)
            return;

        backend.WriteAudioSamples(samples, sourceOffset, sampleCount);
    }
}
