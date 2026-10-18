# 发布

目标预发布为 `v0.1.0-preview.1`；发布前必须完成相应构建、检查和安装验证。

- 输出 Universal、x64、ARM64 三种标准 Windows 安装程序。Universal 自动选择系统架构，必要的兼容 GPU Worker 单独标明架构。
- 支持仅当前用户和所有用户安装；所有用户安装按 Windows 要求提权。桌面快捷方式作为可选任务，默认不勾选。
- 应用、Worker 和基础引擎按架构配套，检查 PE 类型。模型权重不随源码/安装包发布；可选大型运行时需说明下载和依赖。
- 保留原有用户媒体和数据，升级/卸载不能默认清除模型、录音和对话。不要在测试时修改用户日常安装。
- 验证每个安装包的架构选择、安装目录、快捷方式默认值、启动、升级与卸载。需要用户参与的提权或安全提示不得绕过。
- 检查源码和历史无凭据、个人媒体和构建文件，更新 README、AGENTS、CREDITS、变更说明与已知限制。
- 为发布文件生成 SHA-256 清单，GitHub Release 标记 prerelease，上传三个安装包、校验清单及所需许可信息。未签名状态要如实说明，不承诺消除 SmartScreen 提示。
- 只有完成实际发布并核对下载资产后，才在 README 写成可下载版本。

## 实际命令

```powershell
./scripts/bootstrap-packaging.ps1
# 两个 app 输出必须来自同一源码版本，并完成原生回归；Worker 按架构 publish。
./scripts/package.ps1 -AppOutputRoot artifacts/preview-apps -Architecture All
./scripts/package-sources.ps1
./scripts/test-installer.ps1 -Installer artifacts/release/0.1.0-preview.1/WinWhisper-0.1.0-preview.1-Universal-Setup.exe -PlaybackVideo 'C:/path/to/video.mp4'
# x64、ARM64 包分别重复测试；不要并发运行会共享测试注册表项的安装测试。
```

`package.ps1` 每次创建新的 stage，校验本机 PE 和受控 x64 Worker，拒绝模型权重和凭据文件。安装器使用 NSIS 3.13，便携 ZIP 的哈希由 bootstrap 校验。范围页选择所有用户时交给 Windows 提权；不安装系统服务，不全局修改 PATH。

测试使用独立卸载注册项、快捷方式名和目录标记，覆盖安装、升级、启动及卸载。卸载采用编译时生成的应用文件清单，不递归清除整个目录，也不删除 `%LOCALAPPDATA%/MemoAI.Native`。安装前检查目标应用是否仍在运行。

GitHub 先创建 draft prerelease、上传三种安装程序、`SHA256SUMS.txt` 和源码/许可资料，再手动运行 `.github/workflows/installer-qa.yml`。该工作流在 Windows x64 runner 上验证当前用户和所有用户安装，后者使用 runner 既有权限，测试脚本不会批准 UAC。`native_ui` 只有具备可用桌面时启用；关闭该项时不能声称完成 GUI 验证。通过后才撤销 draft 并保留 prerelease 标记，最后核对资产清单和下载地址。

第三方来源与源码包见 [源码获取](../docs/THIRD-PARTY-SOURCES.md)。签名证书未配置时，不得将安装程序写成已签名，也不得指示绕过 Windows 安全拦截。
