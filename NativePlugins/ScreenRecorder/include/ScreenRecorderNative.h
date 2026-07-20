#pragma once

#include "RecorderConfig.h"

#if defined(_WIN32)
#define SR_API __declspec(dllexport)
#else
#define SR_API __attribute__((visibility("default")))
#endif

extern "C" {
SR_API bool SR_Initialize(const SRRecorderConfig* config);
SR_API bool SR_SubmitFrame(void* nativeTexture, int64_t pts);
SR_API bool SR_WriteAudioSamples(const float* samples, int sampleCount);
SR_API bool SR_Stop();
SR_API bool SR_IsHealthy();
SR_API const char* SR_GetLastError();
}
