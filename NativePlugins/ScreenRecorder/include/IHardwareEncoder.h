#pragma once

#include "RecorderConfig.h"
#include <functional>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/buffer.h>
#include <libavutil/pixfmt.h>
}

class IHardwareEncoder
{
public:
    using PacketCallback = std::function<bool(AVPacket*)>;

    virtual ~IHardwareEncoder() = default;
    virtual bool Initialize(const SRRecorderConfig& config, AVBufferRef* hwFramesContext, AVPixelFormat inputPixelFormat) = 0;
    virtual bool Encode(AVFrame* frame, const PacketCallback& onPacket) = 0;
    virtual bool Flush(const PacketCallback& onPacket) = 0;
    virtual AVCodecContext* GetCodecContext() const = 0;
};
