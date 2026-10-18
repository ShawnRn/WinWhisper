# WinWhisper 工作流

这里记录项目实际采用的开发、验证和发布流程。架构、数据保护和贡献约束以根目录 [AGENTS.md](../AGENTS.md) 为准；验证结果以 [开发进度](../docs/PROGRESS.md) 为准。

| 流程 | 文档 |
|---|---|
| 架构、依赖、引擎和品牌资源 | [开发与构建](development.md) |
| WinUI 布局、G2、主题、导航与交互 | [原生界面](native-ui.md) |
| 模型安装、推理后端、录音与数据生命周期 | [模型与媒体](models-and-media.md) |
| 断句、标点、翻译、问答和导出 | [字幕与 AI](subtitles-and-ai.md) |
| 字幕 Agent、工具、Diff、允许 / 拒绝及持久回滚 | [字幕 Agent 与版本](transcript-agent.md) |
| 隔离测试、真实音频及 Computer Use | [验证](verification.md) |
| Universal / x64 / ARM64 安装与预发布 | [发布](release.md) |

这些文档应随实现更新，不能将计划中的行为写成已验证能力。
