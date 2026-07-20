#include "VideoMuxer.h"
#include "FFmpegError.h"

extern "C" {
#include <libavutil/opt.h>
}

VideoMuxer::~VideoMuxer()
{
    Close();
}

bool VideoMuxer::Initialize(const SRRecorderConfig& config, const AVCodecContext* videoCodecContext)
{
    AVFormatContext* context = nullptr;
    int result = avformat_alloc_output_context2(&context, nullptr, "mp4", config.outputPath);
    if (result < 0 || context == nullptr) { SrSetLastError(SrAvError(result, "allocate MP4 output context")); return false; }
    formatContext_ = context;

    AVStream* videoStream = avformat_new_stream(formatContext_, nullptr);
    if (videoStream == nullptr) { SrSetLastError("Could not allocate the video stream."); return false; }
    videoStreamIndex_ = videoStream->index;
    result = avcodec_parameters_from_context(videoStream->codecpar, videoCodecContext);
    if (result < 0) { SrSetLastError(SrAvError(result, "copy video parameters")); return false; }
    videoStream->time_base = videoCodecContext->time_base;
    videoStream->avg_frame_rate = videoCodecContext->framerate;

    if ((formatContext_->oformat->flags & AVFMT_NOFILE) == 0)
    {
        result = avio_open(&formatContext_->pb, config.outputPath, AVIO_FLAG_WRITE);
        if (result < 0) { SrSetLastError(SrAvError(result, "open output file")); return false; }
    }

    AVDictionary* options = nullptr;
    av_dict_set(&options, "movflags", "+faststart", 0);
    result = avformat_write_header(formatContext_, &options);
    av_dict_free(&options);
    if (result < 0) { SrSetLastError(SrAvError(result, "write MP4 header")); return false; }
    headerWritten_ = true;
    return true;
}

bool VideoMuxer::WriteVideoPacket(AVPacket* packet, AVRational encoderTimeBase)
{
    packet->stream_index = videoStreamIndex_;
    av_packet_rescale_ts(packet, encoderTimeBase, formatContext_->streams[videoStreamIndex_]->time_base);
    const int result = av_interleaved_write_frame(formatContext_, packet);
    av_packet_unref(packet);
    if (result < 0) { SrSetLastError(SrAvError(result, "mux video packet")); return false; }
    return true;
}

bool VideoMuxer::Close()
{
    bool ok = true;
    if (formatContext_ != nullptr)
    {
        if (headerWritten_)
        {
            const int result = av_write_trailer(formatContext_);
            if (result < 0) { SrSetLastError(SrAvError(result, "write MP4 trailer")); ok = false; }
        }
        if (formatContext_->pb != nullptr && (formatContext_->oformat->flags & AVFMT_NOFILE) == 0)
            avio_closep(&formatContext_->pb);
        avformat_free_context(formatContext_);
    }
    formatContext_ = nullptr;
    videoStreamIndex_ = -1;
    headerWritten_ = false;
    return ok;
}
