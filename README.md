# MemoAI Native

面向 Windows 11 ARM64 的本地音视频工作台，采用 WinUI 3、.NET 10 和独立推理进程。无需 Memo 账号，不使用 Memo 安装包中的代码或资源。

## 开发

```powershell
./scripts/bootstrap.ps1
./scripts/build.ps1
./scripts/test.ps1
./scripts/publish.ps1
```

开发 SDK 安装在 `.tools/`，引擎放在 `engines/`，模型放在 `models/`，均不提交 Git。用户数据保存在 `%LOCALAPPDATA%/MemoAI.Native/`。模型需要用户主动下载或选择本地文件。

## 目标

- 原生 ARM64 WinUI 3 媒体库、播放与双语字幕编辑。
- whisper.cpp CUDA / Vulkan / CPU 转写，记录实际使用的设备和后端。
- Ollama 与内置 llama.cpp 本地翻译、校对、总结和问答。
- 批量队列、录音、配音、视频输出、模型管理和本地数据持久化。

实施顺序与验收记录见 [开发进度](docs/PROGRESS.md)。

