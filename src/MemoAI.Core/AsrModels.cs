using System.Text.RegularExpressions;

namespace MemoAI.Core;

public sealed record AsrModel(string Id, string Name, string Family, string Languages, string License,
    string Source, string Asset, long Bytes, string Sha256, string[] LanguageCodes)
{
    public string DirectoryName => Asset.Replace(".tar.bz2", "");
    public ModelDownload Download => new(Id, Name, "k2-fsa/sherpa-onnx", Asset, "本地语音识别", Bytes, Sha256,
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" + Asset);
    public string ManifestPath(string root) => Path.Combine(root, "asr", Id, "model.asr.json");
}

public sealed record AsrInstalledFile(string Path, long Bytes);
public sealed record AsrInstallation(int Version, string Id, string Sha256, AsrInstalledFile[] Files);

public static class AsrModels
{
    public static readonly AsrModel[] All =
    [
        new("sensevoice-int8", "阿里 SenseVoice Small · INT8", "sensevoice", "中文 / 粤语 / 英语 / 日语 / 韩语", "FunASR Model License", "https://huggingface.co/FunAudioLLM/SenseVoiceSmall", "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09.tar.bz2", 165783878, "7305f7905bfcf77fa0b39388a313f3da35c68d971661a65475b56fb2162c8e63", ["zh", "yue", "en", "ja", "ko"]),
        new("paraformer-int8", "阿里 Paraformer 中文 · INT8", "paraformer", "中文", "Apache 2.0", "https://huggingface.co/funasr/paraformer-zh", "sherpa-onnx-paraformer-zh-int8-2025-10-07.tar.bz2", 228262632, "a071ee5419e14adb34d7f970ab98105a45e6608018b168f023ca2e4810744abe", ["zh"]),
        new("parakeet-v3", "NVIDIA Parakeet TDT v3 · INT8", "transducer", "25 种欧洲语言（不含中文）", "CC BY 4.0", "https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3", "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2", 487170055, "5793d0fd397c5778d2cf2126994d58e9d56b1be7c04d13c7a15bb1b4eafb16bf", ["bg", "hr", "cs", "da", "nl", "en", "et", "fi", "fr", "de", "el", "hu", "it", "lv", "lt", "mt", "pl", "pt", "ro", "ru", "sk", "sl", "es", "sv", "uk"]),
        new("parakeet-v2", "NVIDIA Parakeet TDT v2 · INT8", "transducer", "英语", "CC BY 4.0", "https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2", "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8.tar.bz2", 482468385, "157c157bc51155e03e37d2466522a3a737dd9c72bb25f36eb18912964161e1ad", ["en"]),
        new("parakeet-ja", "NVIDIA Parakeet 日语 · INT8", "transducer", "日语", "CC BY 4.0", "https://huggingface.co/nvidia/parakeet-tdt_ctc-0.6b-ja", "sherpa-onnx-nemo-parakeet-tdt_ctc-0.6b-ja-35000-int8.tar.bz2", 489389564, "4b0a800ef29f4f4c8667339bf6f60d5bfdc2852ddc9dc5741aea65b6f8d1306b", ["ja"]),
        new("qwen3-asr", "阿里 Qwen3-ASR 0.6B · INT8", "qwen3", "中文及方言 / 英语等 30 种语言", "Apache 2.0", "https://huggingface.co/Qwen/Qwen3-ASR-0.6B", "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2", 878702423, "393f8a14e2f5fb96746aaab342997a40641001fbd5bf9592a080a8329178ee96", ["zh", "en", "yue", "ar", "de", "fr", "es", "pt", "id", "it", "ko", "ru", "th", "vi", "ja", "tr", "hi", "ms", "nl", "sv", "da", "fi", "pl", "cs", "fil", "fa", "el", "hu", "mk", "ro"]),
        new("funasr-nano", "阿里 FunASR Nano · INT8", "funasr", "中文 / 英语 / 日语", "Apache 2.0", "https://huggingface.co/FunAudioLLM/Fun-ASR-Nano-2512", "sherpa-onnx-funasr-nano-int8-2025-12-30.tar.bz2", 841730611, "eb43d7ccc2e86b243f6a03b7df361033dda66db9523d1a92bf6aca2b50c9476b", ["zh", "en", "ja"]),
        new("sensevoice-fp32", "阿里 SenseVoice Small · FP32", "sensevoice", "中文 / 粤语 / 英语 / 日语 / 韩语", "FunASR Model License", "https://huggingface.co/FunAudioLLM/SenseVoiceSmall", "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2025-09-09.tar.bz2", 886547748, "bce28acdb0fb3a5b1e0b93f8b202e904ea1d2eca4f3a4c0cd5006626f10d540e", ["zh", "yue", "en", "ja", "ko"]),
        new("paraformer-fp32", "阿里 Paraformer 中文 · FP32", "paraformer", "中文", "Apache 2.0", "https://huggingface.co/funasr/paraformer-zh", "sherpa-onnx-paraformer-zh-2025-10-07.tar.bz2", 822554565, "934e70708301ad31cb15d6a31ff843059c1b22f494f2492249febd94049586ef", ["zh"]),
        new("funasr-nano-fp16", "阿里 FunASR Nano · FP16", "funasr", "中文 / 英语 / 日语", "Apache 2.0", "https://huggingface.co/FunAudioLLM/Fun-ASR-Nano-2512", "sherpa-onnx-funasr-nano-fp16-2025-12-30.tar.bz2", 1030076153, "a07a996361aa2f8b2c4f47861fe01953b5509664efa3392b734580b1eeb362e3", ["zh", "en", "ja"]),
    ];

    public static bool IsManifest(string path) => path.EndsWith(".asr.json", StringComparison.OrdinalIgnoreCase);
    public static AsrModel ReadInstallation(string manifest)
    {
        var installed = Json.Read<AsrInstallation>(File.ReadAllText(manifest));
        var model = All.FirstOrDefault(m => m.Id == installed.Id) ?? throw new InvalidDataException("未知的语音模型包。");
        if (installed.Version != 1 || installed.Sha256 != model.Sha256 || installed.Files is not { Length: > 0 })
            throw new InvalidDataException("模型安装记录无效，请在模型商店重新下载。");
        var root = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        foreach (var file in installed.Files)
        {
            var path = SafePath(root, file.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes) throw new InvalidDataException("模型文件缺失或大小已改变，请重新下载：" + file.Path);
        }
        _ = RequiredFiles(model, ContentRoot(manifest, installed));
        return model;
    }
    public static bool IsInstalled(AsrModel model, string root)
    {
        try { return ReadInstallation(model.ManifestPath(root)).Id == model.Id; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException) { return false; }
    }
    private static string ContentRoot(string manifest, AsrInstallation installed)
    {
        var first = installed.Files[0].Path.Replace('\\', '/').Split('/')[0];
        if (!first.StartsWith("content-", StringComparison.Ordinal) || installed.Files.Any(f => !f.Path.Replace('\\', '/').StartsWith(first + "/", StringComparison.Ordinal)))
            throw new InvalidDataException("模型安装内容路径不一致。");
        return SafePath(Path.GetDirectoryName(Path.GetFullPath(manifest))!, first);
    }
    public static Dictionary<string, string> ResolveFiles(string manifest) => RequiredFiles(ReadInstallation(manifest), ContentRoot(manifest, Json.Read<AsrInstallation>(File.ReadAllText(manifest))));
    public static string SafePath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("模型包包含不安全的文件路径。");
        return path;
    }
    public static Dictionary<string, string> RequiredFiles(AsrModel model, string root)
    {
        // Only known layouts from the pinned model archives; never guess an arbitrary ONNX file.
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(p => !p.Contains("test_wavs", StringComparison.OrdinalIgnoreCase)).ToArray();
        string Find(string pattern)
        {
            var matches = files.Where(p => Regex.IsMatch(Path.GetFileName(p), pattern, RegexOptions.IgnoreCase)).ToArray();
            var quantized = matches.Where(p => p.Contains("int8", StringComparison.OrdinalIgnoreCase) || p.Contains("fp16", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (quantized.Length == 1) return quantized[0];
            if (matches.Length != 1) throw new InvalidDataException($"{model.Name} 文件布局不匹配：{pattern}");
            return matches[0];
        }
        var result = new Dictionary<string, string>();
        switch (model.Family)
        {
            case "sensevoice": case "paraformer": result["model"] = Find(@"^model(?:\..*)?\.onnx$"); break;
            case "transducer":
                result["encoder"] = Find(@"^encoder(?:[.-].*)?\.onnx$"); result["decoder"] = Find(@"^decoder(?:[.-].*)?\.onnx$"); result["joiner"] = Find(@"^joiner(?:[.-].*)?\.onnx$"); break;
            case "qwen3":
                result["frontend"] = Find(@"^conv_frontend\.onnx$"); result["encoder"] = Find(@"^encoder(?:\..*)?\.onnx$"); result["decoder"] = Find(@"^decoder(?:\..*)?\.onnx$");
                result["tokenizer"] = Path.GetDirectoryName(Find(@"^vocab\.json$"))!;
                _ = Find(@"^merges\.txt$"); _ = Find(@"^tokenizer_config\.json$"); break;
            case "funasr":
                result["encoder"] = Find(@"^encoder_adaptor(?:\..*)?\.onnx$"); result["llm"] = Find(@"^llm(?:\..*)?\.onnx$"); result["embedding"] = Find(@"^embedding(?:\..*)?\.onnx$");
                result["tokenizer"] = Path.GetDirectoryName(Find(@"^vocab\.json$"))!; _ = Find(@"^merges\.txt$"); break;
            default: throw new NotSupportedException("未接入的语音模型类型。");
        }
        if (model.Family is not ("qwen3" or "funasr")) result["tokens"] = Find(@"^tokens\.txt$");
        return result;
    }

    public static async Task InstallAsync(AsrModel model, string root, IProgress<double>? progress, CancellationToken ct)
    {
        var destination = Path.GetDirectoryName(model.ManifestPath(root))!;
        Directory.CreateDirectory(destination);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
        if (drive.AvailableFreeSpace < model.Bytes * 5 + 268435456) throw new IOException("模型下载和解压所需空间不足（预留下载大小的 5 倍）。");
        using var downloader = new ModelDownloader();
        var archive = await downloader.DownloadAsync(model.Download, new(model.Bytes, model.Sha256), Path.Combine(root, "downloads"), progress, ct);
        var staging = Path.Combine(destination, "staging-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        try
        {
            var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
            var listing = await Processes.RunAsync(tar, ["-tf", archive], ct: ct); Processes.EnsureSuccess(listing, "检查模型包");
            foreach (var line in listing.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) _ = SafePath(staging, line.TrimEnd('\r', '/'));
            var detail = await Processes.RunAsync(tar, ["-tvf", archive], ct: ct); Processes.EnsureSuccess(detail, "检查模型文件类型");
            if (detail.Output.Split('\n').Any(l => l.Length > 0 && l[0] is not ('-' or 'd'))) throw new InvalidDataException("模型包包含链接或特殊文件，拒绝解压。");
            Processes.EnsureSuccess(await Processes.RunAsync(tar, ["-xf", archive, "-C", staging], ct: ct), "解压模型包");
            _ = RequiredFiles(model, staging);
            var content = Path.Combine(destination, "content-" + Guid.NewGuid().ToString("N")); Directory.Move(staging, content);
            var installed = new AsrInstallation(1, model.Id, model.Sha256, Directory.GetFiles(content, "*", SearchOption.AllDirectories)
                .Select(f => new AsrInstalledFile(Path.GetRelativePath(destination, f), new FileInfo(f).Length)).ToArray());
            var pending = model.ManifestPath(root) + ".pending";
            await File.WriteAllTextAsync(pending, Json.Write(installed), ct); File.Move(pending, model.ManifestPath(root), true);
            progress?.Report(1);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
