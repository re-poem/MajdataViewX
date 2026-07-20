#pragma once

#include "ITextureBridge.h"

#if defined(_WIN32)
#include <d3d11.h>
#include <wrl/client.h>
#endif

class D3D11TextureBridge final : public ITextureBridge
{
public:
    ~D3D11TextureBridge() override;
    bool Initialize(const SRRecorderConfig& config) override;
    AVFrame* AcquireFrame(void* nativeTexture, int64_t pts) override;
    void ReleaseFrame(AVFrame* frame) override;
    AVBufferRef* GetHardwareFramesContext() const override { return hwFramesContext_; }
    AVPixelFormat GetHardwarePixelFormat() const override { return AV_PIX_FMT_D3D11; }

private:
    int width_ = 0;
    int height_ = 0;
    AVBufferRef* hwDeviceContext_ = nullptr;
    AVBufferRef* hwFramesContext_ = nullptr;
#if defined(_WIN32)
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> immediateContext_;
#endif
};
