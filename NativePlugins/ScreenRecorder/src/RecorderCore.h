#pragma once

#include "FFmpegEncoder.h"
#include "ITextureBridge.h"
#include "VideoMuxer.h"
#include <memory>

class RecorderCore
{
public:
    bool Initialize(const SRRecorderConfig& config);
    bool SubmitFrame(void* nativeTexture, int64_t pts);
    bool WriteAudioSamples(const float* samples, int sampleCount);
    bool Stop();
    bool IsHealthy() const { return healthy_; }

private:
    bool healthy_ = true;
    std::unique_ptr<ITextureBridge> textureBridge_;
    std::unique_ptr<IHardwareEncoder> encoder_;
    std::unique_ptr<VideoMuxer> muxer_;
};
