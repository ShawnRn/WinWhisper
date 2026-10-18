namespace MemoAI.Core;

public sealed record StoreModel(string Id, string Name, string Category, string Subtitle, string Description,
    string Size, string Memory, string Badge, ModelDownload? Download = null,
    string? Repository = null, string? Tag = null, string? Asset = null, AsrModel? Asr = null);

public static class StoreModels
{
    public static readonly StoreModel[] All =
    [
        ..AsrModels.All.Select(m => new StoreModel(m.Id, m.Name, "音视频转写", m.Languages,
            "sherpa-onnx · ARM64 / x64 · CPU · 短片段时间戳。首次使用请对照原音检查识别效果。",
            $"{m.Bytes / 1048576d:0} MB", m.Bytes > 800000000 ? "建议 8 GB+ 内存" : "建议 4 GB+ 内存", "ONNX", Asr: m)),
        new("whisper-turbo", "Whisper large-v3-turbo", "音视频转写", "中英混合 · 多语言 · CUDA 加速", "日常视频与播客的首选，在识别效果与速度之间取得平衡。", "约 1.6 GB", "建议 8 GB+ 内存", "推荐", ModelCatalog.Items.First(m => m.Id == "whisper-turbo")),
        new("whisper-large", "Whisper large-v3", "音视频转写", "中英混合 · 多语言 · 精度优先", "用于复杂口音、噪声与重要素材。模型更大，处理时间通常更长。", "约 3.1 GB", "建议 12 GB+ 内存", "高精度", ModelCatalog.Items.First(m => m.Id == "whisper-large")),
        new("whisper-small", "Whisper small", "音视频转写", "多语言 · 轻量转写", "更小的下载与内存占用，适合轻量设备或快速整理清晰录音。", "约 488 MB", "建议 4 GB+ 内存", "轻量", ModelCatalog.Items.First(m => m.Id == "whisper-small")),
        new("whisper-tiny", "Whisper tiny", "音视频转写", "多语言 · 快速试用", "快速验证转写工作流；对复杂中英混合和噪声的识别能力有限。", "约 78 MB", "建议 2 GB+ 内存", "快速", ModelCatalog.Items.First(m => m.Id == "whisper-tiny")),
        new("vad", "Silero VAD", "语音辅助", "静音检测 · 转写辅助", "跳过长静音，减少无语音区域的重复和幻觉。与 Whisper 模型配合使用。", "不到 1 MB", "低内存占用", "辅助", ModelCatalog.Items.First(m => m.Id == "vad")),
        new("qwen", "Qwen3 8B · Q4_K_M", "翻译与总结", "本地 GGUF · 中英翻译 · 总结与问答", "由内置 llama.cpp 运行，无需启动 Ollama。下载后自动切换到内置本地模型。", "约 5 GB", "建议 12 GB+ 内存", "独立运行", ModelCatalog.Items.First(m => m.Id == "qwen")),
        new("kokoro", "Kokoro 中英配音", "配音", "中文 + English · 多音色 · 离线语音合成", "把字幕合成为语音，并按字幕时间自动对齐。下载后自动配置模型、音色与词典。", "约 350 MB", "建议 4 GB+ 内存", "配音", Repository: "k2-fsa/sherpa-onnx", Tag: "tts-models", Asset: "kokoro-multi-lang-v1_1.tar.bz2"),
        new("speakers", "说话人识别套装", "语音辅助", "Pyannote 分段 + 3D-Speaker 声纹", "为已有字幕标记不同说话人。完整下载并配置分段和声纹两个模型。", "约 120 MB", "建议 8 GB+ 内存", "说话人")
    ];
    public static bool IsInstalled(StoreModel model, AppSettings settings, string root) => model.Asr is { } asr ? AsrModels.IsInstalled(asr, root) : model.Id switch
    {
        "kokoro" => File.Exists(settings.TtsModelPath) && File.Exists(settings.TtsVoicesPath) && File.Exists(settings.TtsTokensPath),
        "speakers" => File.Exists(settings.SpeakerSegmentationModel) && File.Exists(settings.SpeakerEmbeddingModel),
        _ => model.Download is not null && File.Exists(Path.Combine(root, model.Download.FileName)) && new FileInfo(Path.Combine(root, model.Download.FileName)).Length == model.Download.Size
    };
    public static bool IsApplied(StoreModel model, AppSettings settings, string root) => model.Asr is { } asr ?
        IsInstalled(model, settings, root) && settings.TranscriptionModelPath.Equals(asr.ManifestPath(root), StringComparison.OrdinalIgnoreCase) : model.Id switch
    {
        "kokoro" or "speakers" => IsInstalled(model, settings, root),
        "vad" => settings.EnableVad && File.Exists(settings.VadModelPath) && Path.GetFullPath(settings.VadModelPath).Equals(Path.GetFullPath(Path.Combine(root, model.Download!.FileName)), StringComparison.OrdinalIgnoreCase),
        "qwen" => settings.ModelProvider == LanguageModelKind.BuiltIn && File.Exists(settings.GgufPath) && Path.GetFullPath(settings.GgufPath).Equals(Path.GetFullPath(Path.Combine(root, model.Download!.FileName)), StringComparison.OrdinalIgnoreCase),
        _ => model.Download is not null && File.Exists(settings.TranscriptionModelPath) && Path.GetFullPath(settings.TranscriptionModelPath).Equals(Path.GetFullPath(Path.Combine(root, model.Download.FileName)), StringComparison.OrdinalIgnoreCase)
    };
    public static void Apply(StoreModel model, AppSettings settings, string root)
    {
        if (!IsInstalled(model, settings, root)) throw new FileNotFoundException("模型还没有完整下载。");
        if (model.Asr is { } asr)
        {
            settings.TranscriptionModelPath = asr.ManifestPath(root);
            settings.Backend = ComputeBackend.Auto;
            if (settings.Language != "auto" && !asr.LanguageCodes.Contains(settings.Language)) settings.Language = "auto";
            return;
        }
        var file = model.Download is not null ? Path.Combine(root, model.Download.FileName) : "";
        switch (model.Id)
        {
            case "vad": settings.VadModelPath = file; settings.EnableVad = true; break;
            case "qwen": settings.GgufPath = file; settings.ModelProvider = LanguageModelKind.BuiltIn; break;
            case "kokoro": case "speakers": break;
            default: settings.TranscriptionModelPath = file; break;
        }
    }
}
