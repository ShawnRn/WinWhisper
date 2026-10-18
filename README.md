# WinWhisper

<img src="src/MemoAI.App/Assets/Brand/WinWhisper.svg" width="112" alt="WinWhisper icon" />

面向 Windows 11 ARM64 和 x64 的本地音视频工作台，采用 WinUI 3、.NET 10 和独立推理进程。无需 Memo 账号，不使用 Memo 安装包中的代码或资源。

## 能做什么

- 导入本地音视频、SRT / VTT 字幕，或粘贴在线媒体链接；下载后可自动排队转写。
- 使用 whisper.cpp 在 CUDA、Vulkan、CPU 之间选择，显示实际后端、设备、耗时和进度；按媒体保存语言、模型及细分参数。
- 模型商店包含 Parakeet v2 / v3 / 日语版、SenseVoice Small、Paraformer、FunASR Nano、Qwen3-ASR 的 ONNX 包，支持校验下载、解压、应用及检查点。ONNX 自动尝试 CUDA → DirectML → CPU，按执行记录确认 GPU；ARM64 的 CUDA 使用独立 x64 兼容 Worker。不同量化版本的限制见 [GPU 支持](docs/GPU.md)。
- 商店按转写、翻译与对话、配音、语音辅助四种用途分开浏览，默认只显示转写模型；搜索和已下载筛选针对当前分类。
- 模型下载与应用统一在商店完成；设置页只保留参数、服务和已有文件路径配置。
- 播放、定位、搜索和编辑双语字幕，调整时间、拆分合并及撤销；媒体库支持列表和缩略图。
- 通过 Ollama 或应用管理的 llama.cpp 翻译、总结、校对、生成章节和思维导图，并围绕内容多轮提问。聊天支持原生 Markdown、流式跟随和可点击时间引用。
- 英语断句保留可用的词时间戳，结合原音频停顿选择边界；可选本地 AI 恢复标点，拒绝改词或漏词。旧字幕没有可靠词时间戳时保留整段时间。
- 字幕翻译明确选择全文、未翻译或选中范围，以及目标语言；默认全文。可选兼容 Chat Completions 的 API，每次发送前确认目的地址和字幕范围，密钥保存到 Windows 凭据管理器。
- 导出 SRT、VTT、TXT、ASS 时可选择双语、仅原文或仅译文。仅译文要求翻译完整，避免静默丢失字幕。
- 按需下载、校验、应用模型；录制音频，调用本地语音模型配音及标记说话人；导出字幕、文本、笔记和视频。
- 录音支持系统内录、麦克风、混合及单个 App 进程树；可选择本地模型和语言边录边转，音频和已完成文字保存在本机。
- 确认删除媒体后清理会话缓存、字幕、对话与任务历史；应用生成的录音随之删除，外部原始文件及其他条目仍引用的录音保留。

AI 默认留在本机。联网用于用户触发的模型/引擎下载、在线媒体导入、明确配置并确认的字幕 API 翻译与第三方导出。浏览器 Cookie 读取是可选操作，受浏览器加密机制限制；不会默认读取或记录 Cookie 值。说明见 [在线导入](docs/ONLINE-IMPORT.md)。

## 快速使用

1. 运行对应架构的 `WinWhisper.exe`，导入文件或粘贴链接。
2. 在「模型商店」下载并应用转写模型，或在「转写设置」选择本地模型；大模型不包含在源码仓库中。
3. 在媒体工作区选择语言、后端和参数，点击「开始转写」。完成后可以编辑、翻译、问答和导出。

聊天使用 Enter 发送、Shift+Enter 换行，并有输入法组词保护。媒体工作区空格播放/暂停，输入文字时不抢占空格。时间引用点击后定位并播放。Alt+← / → 和鼠标侧键用于返回 / 前进。常用操作包括 Ctrl+O 导入、Ctrl+F 搜索字幕及 Ctrl+Z 撤销。

默认使用稳定纯色背景；可在设置中开启 Mica。长媒体问答会核对全部转写片段后回答，结果仍需与原音频核对。

## 开发

需要 Windows 11、Git、PowerShell，以及能访问 NuGet 的网络环境。脚本安装固定版本的 .NET 10 SDK，不更改全局 SDK；默认按宿主系统选择 ARM64 或 x64。应用依赖由 NuGet 还原，运行所需原生引擎须单独获取。

```powershell
./scripts/bootstrap.ps1
./scripts/fetch-engines.ps1 -Rid win-arm64 -Extended
./scripts/build.ps1
# x64：下载其对应引擎，再构建
./scripts/fetch-engines.ps1 -Rid win-x64 -Extended
./scripts/build.ps1 -Rid win-x64
./scripts/test.ps1
./scripts/test-ui.ps1
```

开发 SDK 安装在 `.tools/`，引擎放在 `engines/`，模型放在 `models/`，均不提交 Git。用户数据沿用 `%LOCALAPPDATA%/MemoAI.Native/`，以保留开发预览期间已有的媒体和对话。模型需要用户主动下载或选择本地文件。ARM64 输出为 `src/MemoAI.App/bin/ARM64/Release/net10.0-windows10.0.26100.0/win-arm64/WinWhisper.exe`，x64 位于对应的 `x64` / `win-x64` 目录。

`fetch-engines.ps1` 下载按架构区分的 FFmpeg、whisper.cpp、yt-dlp、Deno，以及 `-Extended` 指定的 llama.cpp / sherpa-onnx。来源与校验值固定在 [engine-lock.json](docs/engine-lock.json)；解包部分引擎需要系统 `tar` 和 7-Zip。Vulkan 构建脚本仅在自行编译该后端时使用，额外需要对应 C++ / CMake 工具链。

WinUI 控件模板的 G2 适配由构建目标从本地固定 SDK 生成；生成文件不进入仓库，也不重新授予 MIT 许可。全新克隆会在构建时自动生成，无需手动复制界面文件。

## 项目结构与验证

| 路径 | 职责 |
|---|---|
| `src/MemoAI.App` | WinUI 3 界面、播放器、聊天、模型商店和绘制 |
| `src/MemoAI.Core` | 媒体、存储、模型、推理接口、任务调度与导出 |
| `src/MemoAI.Worker` | 与界面隔离的推理工作进程 |
| `tests/MemoAI.Tests` | 核心行为与显式开启的集成检查 |
| `scripts` | 固定依赖获取、双架构构建、原生界面回归与图标生成 |
| `docs` | 实测、功能现状、交互约定及来源记录 |

`scripts/test.ps1` 默认运行核心检查；真实引擎、Ollama、在线下载等检查通过开关显式开启。`scripts/test-ui.ps1 -Rid win-x64` 可检查 x64 窗口；加入 `-PlaybackVideo 'C:/path/to/video.mp4'` 可以验证加载真实视频后的正常关闭。界面回归使用独立数据目录，报告写入 `artifacts/`，不清理或修改日常媒体库。

交互和验收细节见 [INTERACTIONS.md](docs/INTERACTIONS.md)、[UI-REGRESSION.md](docs/UI-REGRESSION.md)；贡献与自动化开发约定见 [AGENTS.md](AGENTS.md)。

## 目标

- 原生 ARM64 / x64 WinUI 3 媒体库、播放与双语字幕编辑。
- whisper.cpp CUDA / Vulkan / CPU 转写，记录实际使用的设备和后端。
- Ollama 与内置 llama.cpp 本地翻译、校对、总结和问答。
- 批量队列、录音、配音、视频输出、模型管理和本地数据持久化。

实施顺序与验收记录见 [开发进度](docs/PROGRESS.md)。

ONNX 模型按短音频分段处理；支持可靠 token 起止信息的模型保留词时间，缺失信息时保持片段边界。静音过滤采用能量检测，Whisper 的 Silero GGML VAD、beam、温度等参数不用于 ONNX。Parakeet v3 不支持中文。模型范围和实测情况见 [模型商店](docs/MODEL-STORE.md)。x64 目前在 Windows ARM64 兼容环境中验证，尚未在物理 x64 设备验收；长媒体和资源耗尽等专项验收仍在进行。

录音转写每 6 秒提交一个完成的音频片段，并显示待处理数量。ONNX 模型在工作进程内保持载入；Whisper 使用独立 CLI 按片段运行，较大模型可能积压。停止转写会保留录音；停止录音后先保存音频，再完成余下字幕。单个 App 内录使用 Windows 进程音频回环，包含该 App 的子进程，目标没有播放声音时录到静音。详见 [录音说明](docs/RECORDING.md)。

## 许可与致谢

应用代码采用 [MIT](LICENSE)，第三方 SDK、引擎、字体和模型保留各自许可。依赖、设计参考及其使用方式见 [CREDITS.md](CREDITS.md)。图标基于用户提供的 SVG 调整；PNG 和包含 16–256 px 多种尺寸的 ICO 已纳入源码，无需额外工具即可构建。若重新生成图标，使用 Node、sharp 与 `scripts/generate-icons.mjs`。

