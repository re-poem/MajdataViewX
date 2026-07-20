#include "FFmpegEncoder.h"
#include "FFmpegError.h"

extern "C" {
#include <libavutil/opt.h>
}

FFmpegEncoder::~FFmpegEncoder()
{
    if (packet_ != nullptr) av_packet_free(&packet_);
    if (context_ != nullptr) avcodec_free_context(&context_);
}

const AVCodec* FFmpegEncoder::SelectWindowsEncoder()
{
#if defined(_WIN32)
    // Windows priority: NVIDIA NVENC, AMD AMF, Intel QSV. Names stay native-side.
    const char* candidates[] = { "h264_nvenc", "h264_amf", "h264_qsv" };
    for (const char* name : candidates)
        if (const AVCodec* codec = avcodec_find_encoder_by_name(name))
            return codec;
#endif
    return avcodec_find_encoder(AV_CODEC_ID_H264);
}

bool FFmpegEncoder::Initialize(const SRRecorderConfig& config, AVBufferRef* hwFramesContext, AVPixelFormat inputPixelFormat)
{
    const AVCodec* codec = SelectWindowsEncoder();
    if (codec == nullptr) { SrSetLastError("No H.264 hardware encoder is available."); return false; }

    context_ = avcodec_alloc_context3(codec);
    if (context_ == nullptr) { SrSetLastError("Could not allocate the video codec context."); return false; }

    context_->codec_id = codec->id;
    context_->width = config.width;
    context_->height = config.height;
    context_->time_base = AVRational{1, config.framesPerSecond};
    context_->framerate = AVRational{config.framesPerSecond, 1};
    context_->pix_fmt = inputPixelFormat;
    context_->gop_size = config.framesPerSecond * 2;
    context_->max_b_frames = 0;
    context_->codec_tag = 0;
    context_->bit_rate = 12'000'000;

    if (hwFramesContext != nullptr)
    {
        context_->hw_frames_ctx = av_buffer_ref(hwFramesContext);
        if (context_->hw_frames_ctx == nullptr) { SrSetLastError("Could not reference FFmpeg hardware frames context."); return false; }
    }

    av_opt_set(context_->priv_data, "preset", "p1", 0);
    av_opt_set(context_->priv_data, "tune", "ull", 0);

    int result = avcodec_open2(context_, codec, nullptr);
    if (result < 0) { SrSetLastError(SrAvError(result, "open hardware video encoder")); return false; }

    packet_ = av_packet_alloc();
    if (packet_ == nullptr) { SrSetLastError("Could not allocate the encoder packet."); return false; }
    return true;
}

bool FFmpegEncoder::Encode(AVFrame* frame, const PacketCallback& onPacket)
{
    const int result = avcodec_send_frame(context_, frame);
    if (result < 0) { SrSetLastError(SrAvError(result, "send video frame")); return false; }
    return ReceivePackets(onPacket);
}

bool FFmpegEncoder::Flush(const PacketCallback& onPacket)
{
    const int result = avcodec_send_frame(context_, nullptr);
    if (result < 0 && result != AVERROR_EOF) { SrSetLastError(SrAvError(result, "flush video encoder")); return false; }
    return ReceivePackets(onPacket);
}

bool FFmpegEncoder::ReceivePackets(const PacketCallback& onPacket)
{
    while (true)
    {
        const int result = avcodec_receive_packet(context_, packet_);
        if (result == AVERROR(EAGAIN) || result == AVERROR_EOF) return true;
        if (result < 0) { SrSetLastError(SrAvError(result, "receive video packet")); return false; }
        if (!onPacket(packet_)) return false;
    }
}
