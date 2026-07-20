#include "RecorderCore.h"
#include "FFmpegError.h"

#if defined(_WIN32)
#include "platform/windows/D3D11TextureBridge.h"
#endif

namespace {
std::unique_ptr<ITextureBridge> CreateTextureBridge()
{
#if defined(_WIN32)
    return std::make_unique<D3D11TextureBridge>();
#else
    return nullptr;
#endif
}
}

bool RecorderCore::Initialize(const SRRecorderConfig& config)
{
    if (config.outputPath == nullptr || config.width <= 0 || config.height <= 0 ||
        config.framesPerSecond <= 0 || (config.width & 1) != 0 || (config.height & 1) != 0)
    {
        SrSetLastError("Invalid recorder configuration.");
        return false;
    }

    textureBridge_ = CreateTextureBridge();
    if (!textureBridge_) { SrSetLastError("No platform texture bridge is available."); return false; }
    if (!textureBridge_->Initialize(config)) { healthy_ = false; return false; }

    encoder_ = std::make_unique<FFmpegEncoder>();
    if (!encoder_->Initialize(config, textureBridge_->GetHardwareFramesContext(), textureBridge_->GetHardwarePixelFormat()))
    {
        healthy_ = false;
        return false;
    }

    muxer_ = std::make_unique<VideoMuxer>();
    if (!muxer_->Initialize(config, encoder_->GetCodecContext()))
    {
        healthy_ = false;
        return false;
    }

    healthy_ = true;
    return true;
}

bool RecorderCore::SubmitFrame(void* nativeTexture, int64_t pts)
{
    AVFrame* frame = textureBridge_->AcquireFrame(nativeTexture, pts);
    if (frame == nullptr) { healthy_ = false; return false; }

    const bool ok = encoder_->Encode(frame, [this](AVPacket* packet) {
        return muxer_->WriteVideoPacket(packet, encoder_->GetCodecContext()->time_base);
    });
    textureBridge_->ReleaseFrame(frame);
    healthy_ = healthy_ && ok;
    return ok;
}

bool RecorderCore::WriteAudioSamples(const float* samples, int sampleCount)
{
    (void)samples;
    (void)sampleCount;
    // Audio remains on the managed legacy path until the native video pipeline is fully enabled.
    return true;
}

bool RecorderCore::Stop()
{
    bool ok = true;
    if (encoder_)
        ok = encoder_->Flush([this](AVPacket* packet) {
            return muxer_->WriteVideoPacket(packet, encoder_->GetCodecContext()->time_base);
        }) && ok;
    if (muxer_)
        ok = muxer_->Close() && ok;
    textureBridge_.reset();
    encoder_.reset();
    muxer_.reset();
    healthy_ = ok;
    return ok;
}
