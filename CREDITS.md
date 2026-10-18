# Credits 与第三方许可

WinWhisper 的应用代码采用 [MIT](LICENSE)。以下项目、字体、SDK、运行时与模型保留各自的许可，应用的 MIT 不重新授权这些组件。引擎版本、下载地址和 SHA-256 见 [engine-lock.json](docs/engine-lock.json)，NuGet 依赖见各项目的 `packages.lock.json`。

## 应用与媒体能力

| 项目 | 用途 | 上游许可 / 说明 |
|---|---|---|
| [Windows App SDK / WinUI](https://github.com/microsoft/WindowsAppSDK) | 原生窗口、控件、Mica 与主题；控件模板在本机构建时生成 G2 绘制适配，不作为仓库源码分发 | NuGet 包保留 Microsoft Windows App SDK 许可及 NOTICE，文本位于 `src/MemoAI.App/Assets/Licenses/` |
| [Win2D](https://github.com/microsoft/Win2D) | G2 几何路径和图形绘制 | MIT |
| [.NET](https://github.com/dotnet/runtime) | 应用及工作进程运行时 | MIT，附加第三方声明以上游分发为准 |
| [Microsoft Visual C++ Runtime](https://visualstudio.microsoft.com/license-terms/vs2022-cruntime/) | 应用、本地引擎及 Worker 的 app-local CRT，避免当前用户安装依赖全局提权 | Microsoft 运行时条款；保留 EULA 与 Redist 说明，版本和文件哈希见 `docs/msvc-runtime-lock.json` |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM 基础设施 | MIT |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) / [SQLite](https://sqlite.org/copyright.html) | 媒体、字幕、任务和历史保存 | 包及其原生依赖各自保留许可 |
| [Markdig](https://github.com/xoofx/markdig) | 原生聊天 Markdown 解析 | BSD-2-Clause |
| [NAudio](https://github.com/naudio/NAudio) | Windows 系统音频与麦克风录制 | MIT |
| [Windows Process Loopback API](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/) | 单个 App 及子进程内录的接口文档与行为参考 | Windows 系统 API；C# 互操作独立实现，未复制示例应用代码 |
| [FFmpeg](https://ffmpeg.org/legal.html) | 解码、波形、封面、配音对齐和视频输出 | 许可取决于构建选项；当前选用包含 GPL 组件的外部构建，不将其归为应用 MIT 代码 |
| [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) / [tordona/ffmpeg-win-arm64](https://github.com/tordona/ffmpeg-win-arm64) | x64 / ARM64 FFmpeg 构建来源 | 分发时须保留对应构建的许可及源码获取信息 |
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | 用户触发的在线媒体导入与浏览器 Cookie 参数 | 源码 Unlicense；官方打包可执行文件及其依赖保留上游完整声明 |
| [Deno](https://github.com/denoland/deno) | yt-dlp 所需 JavaScript 运行时 | MIT 及其第三方许可 |
| [NSIS](https://nsis.sourceforge.io/) | Unicode 安装与卸载程序，支持账户范围和可选快捷方式 | 保留 NSIS 包内 COPYING 中的各组件许可；便携编译器不要求全局安装 |
| [7-Zip](https://www.7-zip.org/) | 原生工具预压缩安装负载；安装时临时使用固定版本的 7zr 解压 | GNU LGPL / BSD 等上游条款，保留许可及同版源码包；未全局安装压缩工具 |

## 本地 AI

| 项目 | 用途 | 上游许可 / 说明 |
|---|---|---|
| [whisper.cpp / ggml](https://github.com/ggml-org/whisper.cpp) | 本地 Whisper 转写，CUDA / Vulkan / CPU | MIT |
| [OpenAI Whisper](https://github.com/openai/whisper) | 语音模型来源 | MIT |
| [llama.cpp](https://github.com/ggml-org/llama.cpp) | 应用管理的 GGUF 推理 | MIT；模型许可独立 |
| [Ollama](https://github.com/ollama/ollama) | 已运行的本地模型服务集成 | MIT；模型许可独立 |
| [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) | 本地语音合成、说话人处理及扩展语音模型引擎 | Apache-2.0；模型许可独立 |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) / [DirectML](https://github.com/microsoft/DirectML) | ONNX GPU 算子执行；按架构构建 sherpa-onnx 与 DirectML 互操作 | ONNX Runtime 为 MIT；DirectML 遵循 Microsoft.AI.DirectML 包内 LICENSE，分发保留两者声明 |
| [NVIDIA CUDA](https://docs.nvidia.com/cuda/eula/index.html) / [cuDNN](https://docs.nvidia.com/deeplearning/cudnn/backend/latest/reference/eula.html) | 可选 CUDA 13 / cuDNN 9 GPU 运行时，由用户在商店主动下载 | NVIDIA 各组件许可；不属于应用 MIT，保留原始包内的许可与上游链接 |
| [eSpeak NG](https://github.com/espeak-ng/espeak-ng) / [piper-phonemize](https://github.com/rhasspy/piper-phonemize) | sherpa 的 TTS 构建包含的音素处理组件 | eSpeak NG 为 GPL-3.0，piper-phonemize 为 MIT；保留所用源码与各项 COPYING / LICENSE |
| [Silero VAD](https://github.com/snakers4/silero-vad) | 语音活动检测 | MIT |
| [Qwen](https://huggingface.co/Qwen) / [IndexTeam](https://huggingface.co/IndexTeam) | 按需选择的本地翻译、总结模型来源 | 以每个模型的 model card 为准 |
| [Kokoro](https://huggingface.co/hexgrad/Kokoro-82M) / [3D-Speaker](https://github.com/modelscope/3D-Speaker) / [pyannote](https://github.com/pyannote/pyannote-audio) | 按需配音、声纹与分段模型来源 | 代码与模型分别按上游条款使用 |
| [NVIDIA Parakeet](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3) | TDT v2 / v3 / 日语开放语音模型；通过 sherpa-onnx 的 ONNX 转换包运行 | 模型 CC BY 4.0；保留 NVIDIA 的归属信息与 model card |
| [SenseVoice Small](https://huggingface.co/FunAudioLLM/SenseVoiceSmall) | 阿里 / FunAudioLLM 多语言识别 | 自定义 [FunASR Model License](https://github.com/modelscope/FunASR/blob/main/MODEL_LICENSE)，不等同于运行时代码的 Apache 许可 |
| [Paraformer](https://huggingface.co/funasr/paraformer-zh) | 中文语音识别 | model card 标注 Apache 2.0 |
| [FunASR Nano](https://huggingface.co/FunAudioLLM/Fun-ASR-Nano-2512) | 中文、英语、日语识别 | model card 标注 Apache 2.0 |
| [Qwen3-ASR](https://huggingface.co/Qwen/Qwen3-ASR-0.6B) | 多语言识别 | Apache 2.0；ONNX 转换来源见 [sherpa 文档](https://k2-fsa.github.io/sherpa/onnx/qwen3-asr/pretrained.html) 及 Wasser1462/Qwen3-ASR-onnx |

语音模型包来自 [sherpa-onnx asr-models release](https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models)。实际资产名、大小及上游 SHA-256 固定在 `src/MemoAI.Core/AsrModels.cs`；模型权重和解压产物不提交到仓库。应用下载并解压原始模型包，保留其中的 README 等文件；商店链接到各模型的上游说明和许可。

CUDA 及 NVIDIA 运行时保留 NVIDIA 的分发条款。模型不随源码仓库提交，下载时应查看对应来源和许可。

## 设计参考与品牌资源

- [Proma](https://github.com/proma-ai/Proma)：聊天工作台与多轮消息组织的交互参考；WinWhisper 使用自行实现的 WinUI 控件和 Markdig 渲染，没有移植其 Electron / React 应用代码。Proma 上游为 AGPL-3.0。
- [tomakura/yt-dlp-gui](https://github.com/tomakura/yt-dlp-gui)：在线下载、代理及 Cookie 选项的流程参考；WinWhisper 独立实现 yt-dlp 参数和原生界面。上游为 MIT。
- Memo：最初的功能需求参考。未提取或复用 Memo 安装包中的程序代码、商标或资源；WinWhisper 为独立项目。
- [Noto Sans SC](https://github.com/google/fonts/tree/main/ofl/notosanssc)：应用中文字体，SIL OFL 1.1；来源提交、原始哈希和许可随 `Assets/Fonts` 保存。
- WinWhisper 图标：用户提供的 `Whisper-Transcribe-Metro.svg`，在此基础上调整细节并生成平台图标；不来自上述参考应用。

若新增第三方代码、模型或分发文件，应同时更新此清单及对应许可文本。

发布安装程序保留 `licenses/`、各引擎目录和 SDK 资源中的原始声明。第三方源码包、确切提交和构建补丁的获取方式见 [第三方源码](docs/THIRD-PARTY-SOURCES.md)。补充许可文本的来源与 SHA-256 记录在 `distribution/licenses/sources.json`。
