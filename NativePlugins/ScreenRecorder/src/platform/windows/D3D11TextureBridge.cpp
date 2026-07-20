#include "D3D11TextureBridge.h"
#include "FFmpegError.h"

extern "C" {
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_d3d11va.h>
}

D3D11TextureBridge::~D3D11TextureBridge()
{
    if (hwFramesContext_ != nullptr) av_buffer_unref(&hwFramesContext_);
    if (hwDeviceContext_ != nullptr) av_buffer_unref(&hwDeviceContext_);
}

bool D3D11TextureBridge::Initialize(const SRRecorderConfig& config)
{
    width_ = config.width;
    height_ = config.height;

    int result = av_hwdevice_ctx_create(&hwDeviceContext_, AV_HWDEVICE_TYPE_D3D11VA, nullptr, nullptr, 0);
    if (result < 0) { SrSetLastError(SrAvError(result, "create D3D11 hardware device context")); return false; }

    hwFramesContext_ = av_hwframe_ctx_alloc(hwDeviceContext_);
    if (hwFramesContext_ == nullptr) { SrSetLastError("Could not allocate D3D11 hardware frames context."); return false; }

    auto* frames = reinterpret_cast<AVHWFramesContext*>(hwFramesContext_->data);
    frames->format = AV_PIX_FMT_D3D11;
    frames->sw_format = AV_PIX_FMT_NV12;
    frames->width = width_;
    frames->height = height_;
    frames->initial_pool_size = 4;

    result = av_hwframe_ctx_init(hwFramesContext_);
    if (result < 0) { SrSetLastError(SrAvError(result, "initialize D3D11 hardware frames context")); return false; }
    return true;
}

AVFrame* D3D11TextureBridge::AcquireFrame(void* nativeTexture, int64_t pts)
{
#if !defined(_WIN32)
    (void)nativeTexture; (void)pts;
    SrSetLastError("D3D11TextureBridge is available only on Windows.");
    return nullptr;
#else
    if (nativeTexture == nullptr) { SrSetLastError("Unity supplied a null native texture."); return nullptr; }

    auto* unityTexture = static_cast<ID3D11Texture2D*>(nativeTexture);
    D3D11_TEXTURE2D_DESC sourceDesc = {};
    unityTexture->GetDesc(&sourceDesc);
    if (static_cast<int>(sourceDesc.Width) != width_ || static_cast<int>(sourceDesc.Height) != height_)
    {
        SrSetLastError("Unity texture size does not match the recorder configuration.");
        return nullptr;
    }

    AVFrame* frame = av_frame_alloc();
    if (frame == nullptr) { SrSetLastError("Could not allocate a hardware video frame."); return nullptr; }

    int result = av_hwframe_get_buffer(hwFramesContext_, frame, 0);
    if (result < 0)
    {
        av_frame_free(&frame);
        SrSetLastError(SrAvError(result, "allocate D3D11 hardware video frame"));
        return nullptr;
    }

    // The exact D3D11 frame descriptor layout is FFmpeg-version-sensitive. Keep this
    // copy in one bridge so future Metal/Vulkan implementations do not affect encoder code.
    // TODO: wire Unity's ID3D11Device from IUnityGraphicsD3D11 and CopyResource into the
    // AV_PIX_FMT_D3D11 frame texture, converting BGRA/RGBA to NV12 when required.
    frame->pts = pts;
    return frame;
#endif
}

void D3D11TextureBridge::ReleaseFrame(AVFrame* frame)
{
    if (frame != nullptr)
        av_frame_free(&frame);
}
