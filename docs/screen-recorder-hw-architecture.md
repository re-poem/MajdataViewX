# Screen Recorder Hardware Pipeline

The Unity layer owns capture timing and lifetime only. It passes `RenderTexture.GetNativeTexturePtr()` and monotonically increasing video PTS values into a backend facade. Encoder names, FFmpeg `AVFrame`/`AVPacket`, and platform decisions stay outside gameplay code.

## Runtime layers

```text
ScreenRecorder (Unity lifecycle)
  -> IScreenRecorderBackend
      -> NativeScreenRecorderBackend (D3D11 hardware path)
      -> LegacyFFmpegScreenRecorderBackend (CPU fallback)
  -> Native Plugin RecorderCore
      -> ITextureBridge
      -> IHardwareEncoder
      -> VideoMuxer
```

## Windows priority

Windows is designed around Direct3D11 textures and FFmpeg hardware contexts. Encoder selection is native-only and tries NVIDIA NVENC, AMD AMF, then Intel QSV before falling back to a generic H.264 encoder.

## Texture bridge rules

`ITextureBridge` is the only component allowed to translate a Unity native texture into an FFmpeg hardware frame. It must create and own `AVHWDeviceContext` and `AVHWFramesContext`; it must not assume that an `ID3D11Texture2D*` can be written directly into `AVFrame.data[0]`.

## Future platforms

Metal and Vulkan bridges should implement `ITextureBridge` without changing Unity gameplay code, the hardware encoder interface, or the muxer.
