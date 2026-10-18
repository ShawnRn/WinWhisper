using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SherpaOnnx;

namespace MemoAI.Core;

public sealed record SherpaRuntime(string Provider, string Rid, string[] Directories);

public static class SherpaRuntimes
{
    public static bool Supports(SherpaRuntime runtime, string modelId) => !(runtime.Provider == "directml" && runtime.Rid == "win-arm64" && modelId == "sensevoice-int8")
        && !(runtime.Provider == "directml" && modelId == "funasr-nano-fp16")
        && !(runtime.Provider == "cuda" && modelId.StartsWith("funasr-nano", StringComparison.Ordinal));
    public static bool SupportsOperation(SherpaRuntime runtime, string operation) => !(runtime.Provider == "directml" && runtime.Rid == "win-arm64" && operation == "tts");
    public static readonly ModelDownload[] CudaDownloads =
    [
        new("sherpa-cuda", "sherpa-onnx CUDA 13", "k2-fsa/sherpa-onnx", "sherpa-onnx-v1.13.8-cuda-13.x-cudnn-9.x-onnxruntime1.28.2-win-x64-cuda.tar.bz2", "GPU 运行时", 478110488, "1702e4a1ee68a07422469a7ec1d304a54b43a345ad888daf8e0b0257004a3273", "https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.8/sherpa-onnx-v1.13.8-cuda-13.x-cudnn-9.x-onnxruntime1.28.2-win-x64-cuda.tar.bz2"),
        new("cuda-runtime", "CUDA 13.4 数学库", "ggml-org/llama.cpp", "cudart-llama-bin-win-cuda-13.4-x64.zip", "GPU 运行时", 423535356, "738f8c251ac22b70c3ae6f83a10cf222725df0395246a2cf58f32bdb85fbe668", "https://github.com/ggml-org/llama.cpp/releases/download/b11716/cudart-llama-bin-win-cuda-13.4-x64.zip"),
        new("cudnn", "cuDNN 9.21.1", "NVIDIA", "cudnn-windows-x86_64-9.21.1.3_cuda13-archive.zip", "GPU 运行时", 394593427, "a511373690548296fb75897a259f58a44f2419c89e848e5c46030ee0bd369bf4", "https://developer.download.nvidia.com/compute/cudnn/redist/cudnn/windows-x86_64/cudnn-windows-x86_64-9.21.1.3_cuda13-archive.zip")
    ];

    public static SherpaRuntime? Find(AppPaths paths, string provider)
    {
        if (provider == "directml")
        {
            var folder = Path.Combine(paths.EngineRoot, AppPaths.Rid, "sherpa-directml");
            return File.Exists(Path.Combine(folder, "sherpa-onnx-c-api.dll")) && File.Exists(Path.Combine(folder, "DirectML.dll"))
                ? new(provider, AppPaths.Rid, [folder]) : null;
        }
        if (provider != "cuda") return null;
        var installed = Path.Combine(paths.DataRoot, "runtimes", "sherpa-cuda-1.13.8");
        if (File.Exists(Path.Combine(installed, "ready.json")))
            return new(provider, "win-x64", CudaDownloads.Select(d => Path.Combine(installed, d.Id)).ToArray());
        var directories = new[] { "sherpa-cuda", "llama-cuda-runtime", "cudnn-9.21.1-cuda13" }.Select(id => Path.Combine(paths.EngineRoot, "win-x64", id)).ToArray();
        return directories.All(Directory.Exists) ? new(provider, "win-x64", directories) : null;
    }

    public static async Task InstallCudaAsync(AppPaths paths, IProgress<double>? progress, CancellationToken ct)
    {
        var destination = Path.Combine(paths.DataRoot, "runtimes", "sherpa-cuda-1.13.8");
        Directory.CreateDirectory(destination);
        var drive = new DriveInfo(Path.GetPathRoot(destination)!);
        if (drive.AvailableFreeSpace < 6L * 1024 * 1024 * 1024) throw new IOException("CUDA 运行时安装需要至少 6 GB 可用空间。");
        using var downloader = new ModelDownloader();
        for (var i = 0; i < CudaDownloads.Length; i++)
        {
            var model = CudaDownloads[i]; var index = i;
            var archive = await downloader.DownloadAsync(model, new(model.Size, model.Sha256), Path.Combine(destination, "downloads"),
                new Progress<double>(p => progress?.Report((index + p * .85) / CudaDownloads.Length)), ct);
            var target = Path.Combine(destination, model.Id);
            if (!File.Exists(Path.Combine(target, "extracted.json")))
            {
                var staging = target + "-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(staging);
                var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
                try
                {
                    var listing = await Processes.RunAsync(tar, ["-tf", archive], ct: ct); Processes.EnsureSuccess(listing, "检查运行时包");
                    foreach (var line in listing.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) _ = AsrModels.SafePath(staging, line.TrimEnd('\r', '/'));
                    var details = await Processes.RunAsync(tar, ["-tvf", archive], ct: ct); Processes.EnsureSuccess(details, "检查运行时文件类型");
                    if (details.Output.Split('\n').Any(l => l.Length > 0 && l[0] is not ('-' or 'd'))) throw new InvalidDataException("运行时包含链接或特殊文件。");
                    Processes.EnsureSuccess(await Processes.RunAsync(tar, ["-xf", archive, "-C", staging], ct: ct), "解压运行时");
                    await File.WriteAllTextAsync(Path.Combine(staging, "extracted.json"), Json.Write(new { model.Sha256 }), ct);
                    Directory.Move(staging, target);
                }
                finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            }
            progress?.Report((i + 1d) / CudaDownloads.Length);
        }
        await File.WriteAllTextAsync(Path.Combine(destination, "ready.json"), Json.Write(CudaDownloads.Select(d => d.Sha256)), ct);
    }

    public static string PrepareWorker(AppPaths paths, SherpaRuntime runtime, string nativeWorker)
    {
        var source = runtime.Rid == AppPaths.Rid ? Path.GetDirectoryName(nativeWorker)! : new[]
        {
            Path.Combine(AppContext.BaseDirectory, "worker-x64"),
            Path.GetFullPath(Path.Combine(paths.EngineRoot, "..", "artifacts", runtime.Rid, "worker"))
        }.FirstOrDefault(p => File.Exists(Path.Combine(p, "WinWhisper.Worker.exe"))) ?? throw new FileNotFoundException("缺少 x64 GPU 兼容 Worker，请重新安装完整的 WinWhisper。");
        PeArchitecture.RequireCompatible(Path.Combine(source, "WinWhisper.Worker.exe"), runtime.Rid);
        var libraries = runtime.Directories.SelectMany(p => Directory.GetFiles(p, "*.dll", SearchOption.AllDirectories))
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(f => new FileInfo(f).Length).First()).ToArray();
        foreach (var required in runtime.Provider == "cuda" ? new[] { "sherpa-onnx-c-api.dll", "onnxruntime.dll", "onnxruntime_providers_cuda.dll", "cudnn64_9.dll", "cublas64_13.dll" } : ["sherpa-onnx-c-api.dll", "onnxruntime.dll", "DirectML.dll"])
            if (!libraries.Any(f => Path.GetFileName(f).Equals(required, StringComparison.OrdinalIgnoreCase))) throw new FileNotFoundException("GPU 运行时不完整，缺少 " + required);
        foreach (var library in libraries) PeArchitecture.RequireCompatible(library, runtime.Rid);
        var version = typeof(SherpaRuntimes).Assembly.ManifestModule.ModuleVersionId.ToString("N");
        var workerHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, "WinWhisper.Worker.exe"))));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version + workerHash + string.Join('|', libraries.Select(f => f + new FileInfo(f).Length + File.GetLastWriteTimeUtc(f).Ticks)))))[..20];
        var destination = Path.Combine(paths.DataRoot, "gpu-workers", runtime.Rid, runtime.Provider, fingerprint);
        if (File.Exists(Path.Combine(destination, "ready.json"))) return Path.Combine(destination, "WinWhisper.Worker.exe");
        var staging = destination + "-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(staging, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            foreach (var library in libraries) File.Copy(library, Path.Combine(staging, Path.GetFileName(library)), true);
            File.WriteAllText(Path.Combine(staging, "ready.json"), Json.Write(new { runtime.Provider, runtime.Rid, version }));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        return Path.Combine(destination, "WinWhisper.Worker.exe");
    }

    public static string Provider => Environment.GetEnvironmentVariable("WINWHISPER_ONNX_PROVIDER") is "cuda" ? "cuda" : Environment.GetEnvironmentVariable("WINWHISPER_ONNX_PROVIDER") is "directml" ? "directml" : "cpu";
    public static string ProfilingProvider(string workDirectory, out string? profileDirectory)
    {
        profileDirectory = null;
        if (Provider == "cpu") return "cpu";
        profileDirectory = Path.Combine(workDirectory, "gpu-profile", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(profileDirectory);
        var file = Path.Combine(profileDirectory, "provider.config");
        File.WriteAllText(file, "ProfilingFilePrefix=" + Path.Combine(profileDirectory, "profile") + "\nLogSeverityLevel=2\n", new UTF8Encoding(false));
        return Provider + ":" + file;
    }
    public static string CompleteProof(string directory)
    {
        var skipped = new List<string>();
        var (gpu, cpu) = ReadProfile(directory, Provider, skipped.Add);
        File.WriteAllText(Path.Combine(directory, "evidence.json"), Json.Write(new { provider = Provider, rid = AppPaths.Rid, gpuExecutions = gpu, cpuExecutions = cpu, incompleteProfiles = skipped }));
        foreach (var file in Directory.GetFiles(directory, "profile*.json")) File.Delete(file);
        return $"{AppPaths.Rid} {Provider} · GPU 已验证" + (cpu > 0 ? "（部分算子使用 CPU）" : "") + (skipped.Count > 0 ? " · 部分记录不可用" : "");
    }
    public static bool IsNativeFailure(string error) => error.Contains("Non-zero status code returned while running", StringComparison.OrdinalIgnoreCase)
        || error.Contains("CUDA failure", StringComparison.OrdinalIgnoreCase) || error.Contains("cuDNN is unavailable", StringComparison.OrdinalIgnoreCase);

    public static (long Gpu, long Cpu) ReadProfile(string directory, string provider, Action<string>? incomplete = null)
    {
        long gpu = 0, cpu = 0; var expected = provider == "cuda" ? "CUDAExecutionProvider" : "DmlExecutionProvider";
        foreach (var file in Directory.GetFiles(directory, "profile*.json"))
        {
            if (new FileInfo(file).Length > 256L * 1024 * 1024) throw new InvalidDataException("GPU 验证记录过大。");
            using var stream = File.OpenRead(file);
            JsonDocument json;
            try { json = JsonDocument.Parse(stream); }
            catch (JsonException) { incomplete?.Invoke(Path.GetFileName(file)); continue; }
            using (json)
            {
            foreach (var e in json.RootElement.EnumerateArray())
                if (e.TryGetProperty("args", out var args) && args.TryGetProperty("provider", out var ep))
                { if (ep.GetString() == expected) gpu++; else if (ep.GetString() == "CPUExecutionProvider") cpu++; }
            }
        }
        if (gpu == 0) throw new InvalidOperationException("没有检测到实际 GPU 算子执行，拒绝将 CPU 回退显示为 GPU 加速。");
        return (gpu, cpu);
    }
}

// Profile the first decoded clip only; retaining profiles for hours would grow without bound.
internal sealed class SherpaAsrSession : IDisposable
{
    private OfflineRecognizerConfig config;
    private readonly string provider = SherpaRuntimes.Provider;
    private OfflineRecognizer? recognizer;
    private string? profileDirectory;
    public string Backend { get; private set; } = "Pending";
    public string Device { get; private set; } = "尚未执行模型";
    public SherpaAsrSession(string manifest, AppSettings settings, string workDirectory)
    {
        config = SherpaTranscription.Configuration(manifest, settings);
        if (provider != "cpu")
        {
            profileDirectory = Path.Combine(workDirectory, "gpu-profile", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(profileDirectory);
            var file = Path.Combine(profileDirectory, "provider.config");
            File.WriteAllText(file, "ProfilingFilePrefix=" + Path.Combine(profileDirectory, "profile") + "\nLogSeverityLevel=2\n", new UTF8Encoding(false));
            config.ModelConfig.Provider = provider + ":" + file;
        }
    }
    public SherpaDecodedResult Decode(float[] samples)
    {
        if (profileDirectory is not null)
        {
            var probeConfig = config;
            probeConfig.ModelConfig.Qwen3Asr.MaxNewTokens = 4;
            probeConfig.ModelConfig.FunAsrNano.MaxNewTokens = 4;
            using (var probe = new OfflineRecognizer(probeConfig))
            using (var clip = probe.CreateStream())
            { clip.AcceptWaveform(16000, samples[..Math.Min(samples.Length, 32000)]); probe.Decode(clip); }
            Backend = provider == "cuda" ? "Cuda" : "DirectML";
            Device = SherpaRuntimes.CompleteProof(profileDirectory);
            profileDirectory = null; config.ModelConfig.Provider = provider;
        }
        recognizer ??= new OfflineRecognizer(config);
        SherpaDecodedResult result;
        using (var stream = recognizer.CreateStream())
        {
            stream.AcceptWaveform(16000, samples); recognizer.Decode(stream);
            // The pinned upstream C# result struct places Durations at tokens_arr's
            // offset. Use the stable JSON C API instead of reinterpreting pointers.
            var pointer = SherpaOnnxGetOfflineStreamResultAsJson(stream.Handle);
            if (pointer == IntPtr.Zero) throw new InvalidDataException("语音引擎没有返回识别结果。");
            try { result = Json.Read<SherpaDecodedResult>(Marshal.PtrToStringUTF8(pointer) ?? throw new InvalidDataException("语音引擎返回了空结果。")); }
            finally { SherpaOnnxDestroyOfflineStreamResultJson(pointer); }
        }
        if (provider == "cpu") { Backend = "Cpu"; Device = AppPaths.Rid + " CPU"; }
        return result;
    }
    public void Dispose() => recognizer?.Dispose();
    [DllImport("sherpa-onnx-c-api", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SherpaOnnxGetOfflineStreamResultAsJson(IntPtr stream);
    [DllImport("sherpa-onnx-c-api", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SherpaOnnxDestroyOfflineStreamResultJson(IntPtr result);
}

internal sealed record SherpaDecodedResult(string Text, string[] Tokens, float[] Timestamps, float[] Durations);
