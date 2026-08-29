<div align="center">
  <img src="https://github.com/user-attachments/assets/383a065e-b9a4-40b6-a06f-720857de883c" width="160px" />
  
  <h1>MajdataView&Edit X</h1>

  
  ![MajdataX Hole(Circle)](https://img.shields.io/badge/MajdataX-Hole(Circle)-50469C)
  [![GitHub Release](https://img.shields.io/github/v/release/re-poem/MajdataViewX?include_prereleases&sort=semver&display_name=release&label=version)](https://github.com/re-poem/MajdataViewX/releases)
  ![license GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)
  [![State-of-the-art Shitcode](https://img.shields.io/static/v1?label=State-of-the-art&message=Shitcode&color=7B5804)](https://github.com/trekhleb/state-of-the-art-shitcode)
</div>

# 注意 / ATTENTION

- **该项目fork自/ fork from:** [MajdataView&Edit](https://github.com/LingFeng-bbben/MajdataView)

<details>
<summary>v5.x版本帮助</summary>

- **扩展包下载 / Extension Package**： [MajMuriDX Release](https://github.com/re-poem/MajdataViewX/releases/tag/MajMuriDX)
- **如果下载了runtime仍无法使用，试试这个链接 / If you still can't use it after downloading the runtime, try this link**: [runtime download](https://dotnet.microsoft.com/zh-cn/download/dotnet/thank-you/runtime-6.0.36-windows-x64-installer)

</details>

## 下载 / Download

<div style="transform: scale(1.7);transform-origin: 0 20%;">

[![Download](https://img.shields.io/badge/Download-Latest_Release-blue?style=for-the-badge)](https://github.com/re-poem/MajdataViewX/releases)


</div>


## 语言切换 / Language

使用设置菜单 / Please use the setting menu 

## 相关链接 / Related Links

- MajdataX 的 QQ 群聊：361736398 (更快地反馈问题)
- Majdata 系 [官方Discord](https://discord.com/invite/AcWgZN7j6K)
- [MajdataNet](https://majdata.net/)
- [MajdataPlay](https://github.com/TeamMajdata/MajdataPlay_Build)

<br/>
<br/>

# 帮助 / Help

## 构建 / Build

### 当前项目已包含FFmpeg Shared Libs，如果需要自己重新编译，按照以下教程：

克隆仓库后，在 Windows 上安装 [MSYS2](https://www.msys2.org/)，然后在项目根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\ffmpeg-builder\build.ps1
```

构建完成后，将 `ffmpeg-builder/dist/win-x64` 中的所有 DLL 复制到 `Assets/Plugins/x86_64`（与 `RenderingOut.dll` 同目录），再使用 Unity 打开项目。

### RenderingOut.dll见[re-poem/RenderingOut](https://github.com/re-poem/RenderingOut)

<br/>

## 文档 / Documentation

- [macOS native build](docs/macos.md)
- [中文 Wiki](https://github.com/LingFeng-bbben/MajdataView/wiki)
- [English Guide On Charting](https://rentry.co/maiguide#making-the-chart)
- [X新功能Wiki(不再维护，即将迁移)](https://github.com/re-poem/MajdataViewX/wiki)

## 分支说明 / Branch Description

| 分支 | 说明 |
|:---:|:---:|
| master | 正常分支，release 时的分支，继续维护原汁原味 Majdata Edit&View，[下载](https://github.com/re-poem/MajdataViewX/releases) |
| yours | 旧版 yours 分支，从 v4.4.0 修改而来，[你知道的](https://www.bilibili.com/video/BV16UYhzdED7/) |

## 已知问题 / Known Issues

1. **不支持动态比特率的 mp3 文件**
2. **部分语法规则较为宽松，可以在 Majdata 中运行的谱面可能无法在其他软件中（如 maipad、simai、Astro）运行**

<details>
<summary>v5.x版本会出现的问题</summary>

- 软件渲染可能不支持 3:00 以上的歌，可以缩小下面的预览图解决（很难修！！）
- 进行铺面共享时，请不要一次性更改太多内容，比如Ctrl+A+BackSpace（），会导致谱面同步延迟甚至失败
- 在v5.0.0之后的版本，~由于MS的魅力代码~，maj可能会出现无法剪切/复制卡顿等情况，这大概率是因为你后台有远程这种会读取剪贴板的软件造成的，请尝试关掉或调整其设置。

</details>

> 其他问题见 [MajdataX 的 issue 页面](https://github.com/re-poem/MajdataViewX/issues) 或者 **[提交 Issue](https://github.com/re-poem/MajdataViewX/issues/new)**


## 导出说明 / Export Description

| 编码器 | 码控模式 | Low (0) | Medium (1) | High (2) | Ultra (3) |
|---|---|---|---|---|---|
| libx264   | CRF   | 28 | 23 | 18 | 14 |
| h264_nvenc | CQ    | 30 | 24 | 18 | 14 |
| h264_qsv  | ICQ   | 32 | 25 | 18 | 14 |
| h264_amf  | QVBR  | 30 | 24 | 18 | 14 |
| h264_mf   | 码率 (Mbps @1080p60) | 4 | 8 | 16 | 32 |

<br/>
<br/>

# Credits

### Main Programmer

- **[bbben](https://github.com/LingFeng-bbben)**
- **[Moying-moe](https://github.com/Moying-moe/maimaiMuriDetector)**
- **[Lezi](https://github.com/LeZi9916)**
- **[Minepig](https://github.com/Minepig)**
- **RE_POEM**

### Contributors

- **Mirroring** from **[Wh1tyEnd](https://github.com/Wh1tyEnd)**
- **Hanabi Effect** from **青山散人**
- **Slide Generating / SlideCode Support** from **[Minepig](https://github.com/Minepig)**

### Special Thanks

- **Simai** developed by **[Celeca](https://twitter.com/formiku39854)**
- **MaiMuriDX** developed by **[Minepig](https://github.com/Minepig)**
- **MajdataMine** developed by **[RevoBleug](https://github.com/RevoBleug)**
- **MajSimai** developed by **[bbben](https://github.com/LingFeng-bbben/MajSimai)** & **[Lezi](https://github.com/LeZi9916)**
- **RenderingOut** developed by **me**

<br/>
<br/>

---

<p align="center">
Contributions welcome.⭐ If it helps, consider starring the repo.
</p>
