# Repository instructions

- Build a real Windows ARM64 and x64 WinUI 3 desktop application; do not replace the UI with a web application.
- Keep media, inference, and model downloads off the UI thread. Run native inference in separate processes.
- Default to local inference. Never silently upload media or transcripts to external AI services.
- Do not commit models, SDKs, native engine binaries, personal media, credentials, or generated outputs.
- Use pinned dependencies and record engine download sources and SHA-256 hashes.
- Keep progress in docs/PROGRESS.md honest; distinguish implemented behavior from runtime validation.
- Make local Git commits at coherent, verified milestones. Do not push without user instruction.

