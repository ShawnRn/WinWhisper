# 首次设置

首次启动以原生页面显示欢迎 → 检测与推荐 → 下载配置 → 完成。`setup-state.json` 在每位用户的数据目录记录完成或主动跳过；关闭或失败不标记成功。设置中的「设备检测与首次设置」可重新打开。已有模型、字幕、会话与提案不被清除，成功下载所选全部组件后才更新模型设置。

普通用户默认只看到素材语言、用途明确的推荐方案、下载大小和「一键准备」。CPU / 显卡 / 运算分数、手动模型选择、CUDA、来源许可及依赖清单统一收进默认折叠的「高级选项」，不能把硬件诊断报告当作入门页面。用户不必理解后端或模型量化即可完成设置。

检测通过 `WinWhisper.Worker --setup-probe` 运行，有总超时与取消。CPU 来自系统注册表，物理 / 可用内存来自 GlobalMemoryStatusEx，GPU 来自 DXGI EnumAdapters1 / GetDesc1，排除软件适配器；共享内存仅是上限，不能与独立显存或物理内存相加。DXGI ABI 参考 [Microsoft 文档](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgifactory1-enumadapters1) 及 [适配器结构](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1)。ARM64 和 x64 的结构与进程各自匹配。

短时向量能量运算测试约 0.75 秒，只是 CPU DSP 估计，不代表模型转写速度或 GPU 成功执行。推荐结合总内存、当前可用内存、GPU 容量和素材语言；内存不足先 tiny，常规 small，容量足够可选 turbo，英语可选 Parakeet v3。用户可改选；不按显卡品牌猜模型语言或声称 CUDA 已验证。

推荐按用户要求优先精度：总内存 ≥ 24 GiB、可用内存 ≥ 12 GiB、至少 8 逻辑核心、CPU 运算估计 ≥ 500 M 样本 / 秒，且显卡报告专用容量 ≥ 8 GiB 时，优先完整 Whisper large-v3。共享显存设备需共享上限 ≥ 8 GiB 且当前可用内存 ≥ 16 GiB，不能把共享上限与物理内存相加。资源不足再考虑 turbo / small / tiny；这些容量 / CPU 阈值是保守推荐规则，不是 GPU 模型吞吐实测，也不承诺大模型在所有素材上更优。下载失败不会降低用户当前已用模型。

基础 FFmpeg / Whisper / DirectML 随标准安装包提供。引导验证架构，并限时启动 FFmpeg 与 Whisper 兼容引擎。基础依赖损坏时明确要求完整安装包修复，不下载安装任意系统级工具，也不要求 Python / CUDA Toolkit。Whisper 所需模型与 Silero VAD 可在引导下载；ONNX 可显式选择额外 CUDA 组件，复用商店的固定版本 / SHA-256 / 续传 / 原子安装。Whisper 不下载 ONNX CUDA 组件，已集成的 DirectML 不重复下载。驱动和真实算子兼容性仍以转写任务的后端证明为准。

下载清单在开始前列出大小、来源和许可入口；本地配置与媒体不上传。暂停 / 失败保留已验证文件和续传片段，原设置不变；继续后仅在全部组件成功时应用。CUDA 的临时空间与模型磁盘分别检查。首次设置自身不启动字幕生成或云端 API。

验证包括核心推荐 / 语言 / 共享内存边界 / 固定清单 / 取消 / 状态持久化，以及两种架构真实 Worker 检测和原生引导 / 对话切换回归。物理 x64 未测试时注明 x64 在 ARM64 兼容环境执行。

GPU 性能诊断用 `scripts/benchmark-whisper.ps1` 在独立目录对相同音频与模型比较 4 / 8 / 12 / 16 线程，保留模型加载、编码、采样、解码耗时及识别文本；反转第二轮顺序减轻缓存偏差。不以任务管理器占用率为优化目标，不自动降低 beam size 或并发任务来人为提高 GPU 利用率。基准启动前拒绝与已有 Whisper 进程争用 GPU。
