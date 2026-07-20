#pragma once

#include <cstdint>

struct SRRecorderConfig
{
    const char* outputPath;
    int width;
    int height;
    int framesPerSecond;
    int sampleRate;
    int channels;
    int unityTextureFormat;
};
