# 开发与构建

- 产品名为 WinWhisper；兼容保留 MemoAI.* 项目和命名空间，以及 `%LOCALAPPDATA%/MemoAI.Native` 用户数据。
- WinUI 3 + .NET 10 原生应用，ARM64 / x64 各自构建。媒体解码和推理在工作进程中执行，不在 UI 线程阻塞推理。
- 使用 `scripts/bootstrap.ps1` 固定 SDK；`scripts/fetch-engines.ps1 -Rid <RID> -Extended` 获取固定版本的外部引擎，来源和 SHA-256 记录在 `docs/engine-lock.json`。
- `scripts/build.ps1 -Rid win-arm64` / `win-x64` 构建应用并发布对应 Worker；不要在构建未退出时修改应用源文件，也不要覆盖运行中的程序。
- 未通过检查的改动先输出到 `artifacts/` 中的独立目录，再更新日常程序；不能用「已输出 DLL」代替构建进程成功退出。
- 图标源为用户提供的 SVG，平台 PNG / ICO 由 `scripts/generate-icons.mjs` 生成。字体、SDK、引擎和模型各自保留许可。
- 原创代码 MIT。新增依赖更新 CREDITS；模型、SDK、原生二进制、媒体、凭据和生成输出不进入源码仓库。
