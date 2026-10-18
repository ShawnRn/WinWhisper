# YouTube 下载与本地转写

在「导入在线链接」粘贴 URL，选择视频质量或仅音频，点击「下载并导入」。默认勾选下载完成后自动开始本地转写：使用点击下载时的转写默认配置创建独立任务，下载成功后加入本地队列，并在工作区显示进度。模型未准备好时先提示选择或下载，避免媒体下载完成才发现不能转写。取消自动转写即可只下载到媒体库。

ARM64/x64 引擎目录包含 yt-dlp 2026.08.19、FFmpeg 及 Deno 2.9.7；随附 yt-dlp 已包含 EJS 脚本，不临时获取远程执行组件。程序不依赖用户另外安装 yt-dlp GUI。来源与校验值在 `docs/engine-lock.json`，通过 `scripts/fetch-engines.ps1 -Rid win-arm64`（或 win-x64）准备依赖。

默认不读取 Cookie。可以明确选择 Edge、Chrome、Firefox、Brave、Vivaldi、Opera 或 Chromium，并可填写个人资料名称或路径；选项只保留在当前界面内，由 yt-dlp 原生 `--cookies-from-browser` 处理，不把 Cookie 内容写入数据库、日志或界面。也支持自行选择 Netscape 格式的 Cookie 文件：运行时使用临时副本，结束或取消后删除副本，不修改源文件。媒体请求会按站点正常使用登录状态。

浏览器加密及文件占用可能使 Cookie 读取失败，此时显示原因和重试路径，可改选 Firefox 或提供 Cookie 文件。不会为此更改浏览器保护、要求提升权限或自动关闭浏览器。Chrome 的 App-Bound 加密限制见 [yt-dlp 上游问题](https://github.com/yt-dlp/yt-dlp/issues/10927)。浏览器选项接入与错误分类已经验证；尚未使用当前用户的真实浏览器登录资料进行逐浏览器下载验收。

默认沿用系统代理，也可填写 HTTP/HTTPS/SOCKS5 代理。忽略外部 yt-dlp 配置，避免意外读取其他下载器设置。进度只显示下载比例、速度、解析及合并阶段；失败消息区分网络、登录、Cookie 解密、限流和媒体不可用，不回显凭据。

已实测公开短视频下载 → 创建独立任务 → CUDA 转写，使用分块、分段长度、温度等本页参数得到字幕。运行 `scripts/test.ps1 -Online` 可复现（显式联网、不读取 Cookie）；普通测试保持离线。

参考：[用户提供的 yt-dlp GUI](https://github.com/tomakura/yt-dlp-gui)、[yt-dlp 认证参数](https://github.com/yt-dlp/yt-dlp#authentication-options)、[EJS 与运行时说明](https://github.com/yt-dlp/yt-dlp/wiki/EJS)、[Cookie 文件格式](https://github.com/yt-dlp/yt-dlp/wiki/FAQ#how-do-i-pass-cookies-to-yt-dlp)。当前实现直接集成 yt-dlp，未使用该 GUI 的代码或界面资源。
