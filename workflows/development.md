# 开发与构建

- 产品名为 WinWhisper；兼容保留 MemoAI.* 项目和命名空间，以及 `%LOCALAPPDATA%/MemoAI.Native` 用户数据。
- WinUI 3 + .NET 10 原生应用，ARM64 / x64 各自构建。媒体解码和推理在工作进程中执行，不在 UI 线程阻塞推理。
- 使用 `scripts/bootstrap.ps1` 固定 SDK；`scripts/fetch-engines.ps1 -Rid <RID> -Extended` 获取固定版本的外部引擎，来源和 SHA-256 记录在 `docs/engine-lock.json`。
- `scripts/build.ps1 -Rid win-arm64` / `win-x64` 构建应用并发布对应 Worker；不要在构建未退出时修改应用源文件，也不要覆盖运行中的程序。
- 两个 RID 的应用构建按顺序执行：NuGet 的 `obj/project.assets.json` 是共享文件，同时还原 ARM64 / x64 可能覆盖目标资产，即使输出目录不同也会触发 NETSDK1047。等待前一构建退出后，再还原和构建下一架构。
- 未通过检查的改动先输出到 `artifacts/` 中的独立目录，再更新日常程序；不能用「已输出 DLL」代替构建进程成功退出。
- 图标源为用户提供的 SVG，平台 PNG / ICO 由 `scripts/generate-icons.mjs` 生成。字体、SDK、引擎和模型各自保留许可。
- 原创代码 MIT。新增依赖更新 CREDITS；模型、SDK、原生二进制、媒体、凭据和生成输出不进入源码仓库。

## 发布构建目录

正在使用的 `bin/` 不可覆盖。将应用输出到新的独立目录，显式给出完整 TFM / RID 路径；命令行传入 `OutputPath` 后不要假设 MSBuild 还会自动追加它们：

```powershell
$rid = 'win-arm64' # x64 构建改为 win-x64 / x64
$platform = 'ARM64'
$appOut = Join-Path $PWD "artifacts/preview-apps/$platform/Release/net10.0-windows10.0.26100.0/$rid/"
./.tools/dotnet/dotnet.exe build src/MemoAI.App/MemoAI.App.csproj -c Release -r $rid "-p:Platform=$platform" "-p:OutputPath=$appOut" --self-contained true
# 等待进程退出且退出码为 0，再 publish Worker，并复制到 appOut/worker。
./.tools/dotnet/dotnet.exe publish src/MemoAI.Worker/MemoAI.Worker.csproj -c Release -r $rid --self-contained true -o "artifacts/$rid/worker"
```

DirectML 本地运行时由 `scripts/build-sherpa-directml.ps1 -Rid <RID>` 生成；两个架构分别构建，保留源码补丁及声明。安装包还需要 Visual Studio Build Tools 的固定 app-local CRT，版本和哈希见 `docs/msvc-runtime-lock.json`。只复制对应架构文件；上游 ARM64 CRT 目录中存在 x64 文件，不能不加校验地全量混入 ARM64 应用。

完成两种架构的隔离回归后按 [发布流程](release.md) 打包。打包时保留固定 Windows App SDK x64 分发中的 ARM64EC 配套文件，以确切哈希校验该例外，不能笼统允许混合架构 DLL。
