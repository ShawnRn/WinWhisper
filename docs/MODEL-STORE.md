# 模型商店

商店按用途分为「转写模型」「翻译与对话」「配音」「语音辅助」，默认进入转写模型。Parakeet / Whisper / 阿里 ASR 属于转写；Qwen GGUF 和已有 Ollama 模型放在翻译与对话；Kokoro 属于配音；Silero VAD 和说话人识别属于语音辅助，不与转写模型混排。

搜索只针对当前分类，「仅看已下载」是独立筛选，切换用途后仍保留筛选状态。Ollama 仅在翻译与对话分类读取，并提供就近刷新与读取状态。商店直接列出以下 ASR 模型，不依赖 Ollama 的模型清单。下载由用户点击触发；大模型不包含在程序和源码中。

| 系列 | 版本 | 语言 | 当前运行时 |
|---|---|---|---|
| Whisper | tiny / small / large-v3-turbo / large-v3 | 多语言，含中文 | whisper.cpp；CUDA / Vulkan / CPU |
| NVIDIA Parakeet TDT | 0.6B v2 INT8 | 英语 | CUDA / DirectML / CPU；v2 尚未单独完成 GPU 验收 |
| NVIDIA Parakeet TDT | 0.6B v3 INT8 | 25 种欧洲语言，不含中文 | CUDA / DirectML / CPU，v3 实测通过 |
| NVIDIA Parakeet TDT/CTC | 0.6B 日语 INT8 | 日语 | CUDA / DirectML / CPU；日语包尚未单独完成 GPU 验收 |
| 阿里 SenseVoice Small | INT8 / FP32 | 中文、粤语、英语、日语、韩语 | INT8 CUDA、FP32 DirectML 实测通过；ARM64 INT8 DirectML 不兼容 |
| 阿里 Paraformer | INT8 / FP32 | 中文 | INT8 CUDA / DirectML 实测通过；FP32 GPU 尚未单独验收 |
| 阿里 FunASR Nano | INT8 | 中文、英语、日语 | DirectML / CPU；CUDA 未通过内容校验 |
| 阿里 Qwen3-ASR | 0.6B INT8 | 30 种语言及中文方言 | CUDA / DirectML / CPU，实测通过 |

应用模型后，它成为新媒体的默认转写模型；已有媒体保留自己的设置，可在「转写设置」选择已安装模型，或点击恢复默认设置。下载不更改已经运行的任务。ONNX 使用 CUDA / DirectML，Whisper 使用 CUDA / Vulkan；自动模式可回退 CPU，明确选择 GPU 时不静默回退。GPU 算子仍可能与 CPU 混合执行，详见 [GPU 支持与验证](GPU.md)。FunASR Nano FP16 未通过当前运行时的内容校验，已从商店和可选模型中收起，旧安装记录仍能识别并提示更换 INT8。

ONNX 模型以 20 秒为上限分段，近静音和长停顿可以使用能量检测过滤。它不使用 Whisper 的 Silero GGML 模型；有可靠 token 起止信息时保留真实时间，缺失时保持片段范围，不插值伪造词时间。Whisper 的搜索宽度、采样温度、处理分块和字幕长度控件在选择 ONNX 模型时禁用。

下载使用固定资产大小与 SHA-256，支持 HTTP Range 续传。先验证压缩包，再检查路径与文件类型、解压至暂存目录、检查模型布局，最后原子写入安装记录。暂停或失败不会把半成品标为已安装。载入前再次检查安装记录、文件大小及模型配置；模型缺失时请在商店重新下载。

许可按模型单独显示并链接至上游；特别是 SenseVoice 的自定义模型许可，不能套用 sherpa-onnx 代码的 Apache 2.0。来源及归属见 [Credits](../CREDITS.md)。

## 验证

核心测试检查目录完整性、引擎选择、语言范围、静音过滤、下载续传与损坏拒绝。真实模型测试显式触发下载，示例：

```powershell
./.tools/dotnet/dotnet.exe run --project tests/MemoAI.Tests/MemoAI.Tests.csproj -c Release -- --asr=sensevoice-int8,parakeet-v3,paraformer-int8
```

测试覆盖安装、应用、工作进程转写、检查点恢复及拒绝虚假的 CUDA 标记。其他量化版本和语言需要逐项验收，目录存在不代表已完成全部精度、性能或长媒体测试。报告保存在忽略的 `artifacts/tests/`。

2026-10-18：ARM64 实测通过 SenseVoice INT8、Parakeet v3 INT8、Paraformer INT8、Qwen3-ASR INT8、FunASR Nano INT8；x64 兼容执行实测通过 SenseVoice INT8 与 Parakeet v3 INT8。两种架构均通过连续音频输入、提前返回字幕和取消退出检查。单段样本不能替代完整语种、长媒体或准确率基准测试。
