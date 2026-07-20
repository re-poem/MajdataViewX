#include "ScreenRecorderNative.h"
#include "RecorderCore.h"
#include "FFmpegError.h"
#include <memory>

namespace {
std::unique_ptr<RecorderCore> g_recorder;
}

extern "C" {

bool SR_Initialize(const SRRecorderConfig* config)
{
    if (config == nullptr) { SrSetLastError("Recorder config was null."); return false; }
    g_recorder = std::make_unique<RecorderCore>();
    if (!g_recorder->Initialize(*config))
    {
        g_recorder.reset();
        return false;
    }
    return true;
}

bool SR_SubmitFrame(void* nativeTexture, int64_t pts)
{
    if (!g_recorder) { SrSetLastError("Recorder has not been initialized."); return false; }
    return g_recorder->SubmitFrame(nativeTexture, pts);
}

bool SR_WriteAudioSamples(const float* samples, int sampleCount)
{
    if (!g_recorder) { SrSetLastError("Recorder has not been initialized."); return false; }
    return g_recorder->WriteAudioSamples(samples, sampleCount);
}

bool SR_Stop()
{
    if (!g_recorder) return true;
    const bool ok = g_recorder->Stop();
    g_recorder.reset();
    return ok;
}

bool SR_IsHealthy()
{
    return g_recorder == nullptr || g_recorder->IsHealthy();
}

const char* SR_GetLastError()
{
    return SrGetLastError();
}

}
