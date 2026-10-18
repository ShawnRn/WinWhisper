# 第三方源码与许可

安装目录中的 `licenses/`、`Assets/Licenses/` 和各引擎目录保留原始许可。应用源码的 MIT 不替代这些条款。完整组件清单见 [CREDITS](../CREDITS.md)，引擎的二进制来源与 SHA-256 见 [engine-lock.json](engine-lock.json) 和 [gpu-runtime-lock.json](gpu-runtime-lock.json)。

预发布同时提供 `WinWhisper-0.1.0-preview.1-ThirdPartySources.tar.gz`，下载位置为 [GitHub Release](https://github.com/ShawnRn/WinWhisper/releases/tag/v0.1.0-preview.1)。其中包含：

- sherpa-onnx v1.13.8（`11afbd009a7f8c08f4bcf2fc1b265d0df4670fbf`）及本次 DirectML 构建实际使用的静态依赖源码，包括 eSpeak NG、piper-phonemize、Kaldi、OpenFst 等。包含 DirectML 导入与 profiling 文件名补丁后的源码；补丁在 `scripts/build-sherpa-directml.ps1` 中可复现。
- 本次 FFmpeg ARM64 与 x64 二进制对应的 FFmpeg 源码（`e9b3c55fe0321915a70f44f30b276ef2afdc3a0c`、`94cba3150c6b83057ed48b833689adc806715cc0`），以及相应上游构建脚本和补丁。外部编解码库的获取及配置见这些脚本、FFmpeg 包内 readme/configuration；保留它们各自的许可。
- 固定下载地址、SHA-256 清单和本项目的 DirectML 构建脚本。应用及 Worker 源码可通过同一版本的 GitHub 源码包获取。

自编译语音运行时包含 GPL-3.0 的 eSpeak NG，须连同其 GPL 许可和对应源码获取信息保留；FFmpeg 使用上游 GPL 构建。它们与本地应用通过独立 Worker / CLI 接口交互。模型权重不包含在源码或安装程序中，模型下载由用户在商店发起并保留原始包内说明。

DirectML、Windows App SDK 与 NVIDIA 运行时按各自的上游许可分发；它们不是本项目的 MIT 源码。CUDA/cuDNN 可选 ONNX 运行时由商店按固定版本下载，保留上游包内许可。基础 Whisper / GGUF CUDA 引擎的运行时许可也位于安装目录 `licenses/`。

重新生成发布源码包：先完成双架构原生构建，再运行 `scripts/package-sources.ps1`。该脚本不包含 `.git`、模型下载目录、用户媒体或应用数据。
