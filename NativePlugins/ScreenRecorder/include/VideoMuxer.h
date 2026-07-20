#pragma once

#include "RecorderConfig.h"

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
}

class VideoMuxer
{
public:
    ~VideoMuxer();

    bool Initialize(const SRRecorderConfig& config, const AVCodecContext* videoCodecContext);
    bool WriteVideoPacket(AVPacket* packet, AVRational encoderTimeBase);
    bool Close();

private:
    AVFormatContext* formatContext_ = nullptr;
    int videoStreamIndex_ = -1;
    bool headerWritten_ = false;
};
