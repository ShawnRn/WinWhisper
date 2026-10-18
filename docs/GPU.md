# GPU 支持与验证

Whisper 与内置 GGUF 使用 CUDA / Vulkan / CPU；ONNX 使用 CUDA / DirectML / CPU。自动模式按这个顺序尝试，已知不兼容的组合在启动前提示。手动选择 GPU 时失败会报错，不会把 CPU 执行标成 GPU。

DirectML 随对应架构的引擎构建提供。CUDA ONNX 组件在模型商店的「语音辅助」分类显式下载，约 1.21 GB，至少预留 6 GB 空间；支持暂停续传并验证固定 SHA-256。CUDA 13 需要兼容的 NVIDIA GPU 与驱动。ARM64 UI 保持原生；ONNX CUDA 使用独立 x64 Worker，媒体工具仍匹配宿主架构，避免在 ARM64 进程加载 x64 DLL。程序和共享引擎只读，GPU Worker 缓存在用户数据目录中。

## 当前机器实测

设备为 Windows ARM64 / NVIDIA RTX Spark N1X；CUDA x64 为兼容执行，不能替代物理 x64、AMD、Intel、Qualcomm 显卡验收。表格指短样本内容和执行记录验证，不代表全部语言、量化版本、长媒体或速度基准。

| 模型 | 已验证的 GPU 链路 | 已知限制 |
|---|---|---|
| Parakeet TDT v3 INT8 | CUDA、ARM64 DirectML | v2 和日语包需要各自验收 |
| SenseVoice Small INT8 | CUDA | ARM64 DirectML 会崩溃，启动前拒绝该组合 |
| SenseVoice Small FP32 | ARM64 DirectML | FP32 CUDA 尚未单独验收 |
| Paraformer 中文 INT8 | CUDA、ARM64 DirectML | FP32 尚未单独验收 |
| Qwen3-ASR 0.6B INT8 | CUDA、ARM64 DirectML | 含 CPU 算子，不承诺全部计算都在 GPU |
| FunASR Nano INT8 | ARM64 DirectML | CUDA 结果未通过内容校验；FP16 的 GPU 和 CPU 结果未通过验证，当前版本收起 |
| Kokoro 中英配音 | CUDA | ARM64 DirectML 未通过，自动模式可回退 CPU |
| Pyannote + 3D-Speaker | CUDA、ARM64 DirectML | 分段和声纹不保证每个算子都在 GPU |

Whisper large-v3-turbo 的 CUDA / Vulkan / CPU 实测见 [基准记录](BENCHMARK.md)。轻量的静音检测和媒体处理仍可能使用 CPU。

## 如何确认 GPU

- 首次解码前用最多 2 秒原音频进行受限探测；LLM ASR 探测最多生成 4 个 token。配音使用短句，说话人识别使用短音频。随后保持正式模型载入，完成实际内容处理。
- 读取 ONNX Runtime 的实际执行事件，至少有目标 GPU Provider 的算子执行才确认 GPU；纯 CPU 回退会被拒绝。界面说明部分算子可能使用 CPU，数据不是 GPU 利用率或性能占比。
- 多个 Session 使用不同的 profiling 前缀，防止旧版 ORT 以秒命名时覆盖记录。只有有效记录可作为证据，记录不完整时明确标注；没有 GPU 证据不会宣称加速。
- 探测后保留小型 `gpu-profile/*/evidence.json`，清理较大的原始事件记录。GPU 检查点只有在父进程确认原生进程正常退出后才能复用；复用时标为缓存，不假装再次执行了 GPU。
- 原生错误即使返回退出码 0，也不能作为成功完成。配音先写入待验证文件，父进程确认后再替换最终文件；录音工作进程退出会取消待输入，避免一直等待新片段。

## 重现

```powershell
./scripts/build-sherpa-directml.ps1 -Rid win-arm64
./scripts/build-sherpa-directml.ps1 -Rid win-x64
./scripts/test.ps1 -OnnxGpu 'parakeet-v3:Cuda,sensevoice-fp32:DirectML,funasr-nano:DirectML'
./.tools/dotnet/dotnet.exe run --project tests/MemoAI.Tests/MemoAI.Tests.csproj -c Release -- --speech-gpu=Cuda
./.tools/dotnet/dotnet.exe run --project tests/MemoAI.Tests/MemoAI.Tests.csproj -c Release -- --diarize-gpu=DirectML
```

命名模型的真实测试可能下载模型。引擎版本、来源和校验信息见 [GPU 运行时锁定记录](gpu-runtime-lock.json)，源码构建补丁位于 `scripts/build-sherpa-directml.ps1`。
