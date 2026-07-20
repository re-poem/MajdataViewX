#pragma once

#include "IHardwareEncoder.h"
#include <memory>

class FFmpegEncoder final : public IHardwareEncoder
{
public:
    ~FFmpegEncoder() override;
    bool Initialize(const SRRecorderConfig& config, AVBufferRef* hwFramesContext, AVPixelFormat inputPixelFormat) override;
    bool Encode(AVFrame* frame, const PacketCallback& onPacket) override;
    bool Flush(const PacketCallback& onPacket) override;
    AVCodecContext* GetCodecContext() const override { return context_; }

private:
    bool ReceivePackets(const PacketCallback& onPacket);
    const AVCodec* SelectWindowsEncoder();
    AVCodecContext* context_ = nullptr;
    AVPacket* packet_ = nullptr;
};
