#pragma once

#include "RecorderConfig.h"

extern "C" {
#include <libavutil/frame.h>
#include <libavutil/hwcontext.h>
#include <libavutil/pixfmt.h>
}

class ITextureBridge
{
public:
    virtual ~ITextureBridge() = default;
    virtual bool Initialize(const SRRecorderConfig& config) = 0;
    virtual AVFrame* AcquireFrame(void* nativeTexture, int64_t pts) = 0;
    virtual void ReleaseFrame(AVFrame* frame) = 0;
    virtual AVBufferRef* GetHardwareFramesContext() const = 0;
    virtual AVPixelFormat GetHardwarePixelFormat() const = 0;
};
