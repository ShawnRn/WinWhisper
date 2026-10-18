# ARM64 转写链路实测

2026-10-18，Windows 11 ARM64，NVIDIA RTX Spark N1X，64 GB 统一内存。whisper.cpp b5454，同一 large-v3-turbo 模型和 11 秒 JFK 英文样本，英语、8 线程、关闭 VAD，单次进程运行。CUDA 使用官方 ARM64 CUDA 13.4 引擎；Vulkan 由同一 b5454 源码构建；CPU 使用官方 ARM64 引擎。

| 后端 | 工作进程内转写阶段耗时 | 实时率（耗时/音频时长） | 含 Worker 启动的墙钟耗时 |
|---|---:|---:|---:|
| CUDA | 2.308 秒 | 0.210 | 2.573 秒 |
| Vulkan | 27.363 秒 | 2.488 | 27.809 秒 |
| CPU | 15.081 秒 | 1.371 | 15.366 秒 |

三种后端均真实运行且识别出完整测试句。CUDA 日志确认 `using CUDA0 backend`，Vulkan 确认使用 Vulkan 后端。该样本 CUDA 的转写阶段约为 CPU 的 6.5 倍快；这是单次短音频链路对比，受模型加载、文件缓存和 Vulkan 首次着色器编译影响，不能外推长视频的稳定吞吐。

运行 `./scripts/test.ps1 -Benchmark` 可生成包含完整引擎日志、识别文本及耗时的 `artifacts/tests/<时间>/large-benchmark.json`。本次运行 17/17 项通过（14 项核心检查 + 3 个真实后端）。尚未采集独立加载时间、峰值内存或进行多轮统计，也没有物理 x64 设备性能数据。驱动日志中的 VMM 可寻址容量不能视为物理显存大小。
