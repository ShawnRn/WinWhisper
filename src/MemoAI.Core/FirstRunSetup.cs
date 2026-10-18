using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MemoAI.Core;

public sealed record SetupGpu(string Name, uint Vendor, ulong DedicatedBytes, ulong SharedBytes);
public sealed record SetupDependency(string Name, bool Ready, string Detail);
public sealed record SetupReport(string Cpu, string Architecture, int LogicalCores, ulong MemoryBytes,
    ulong AvailableMemoryBytes, long FreeDiskBytes, SetupGpu[] Gpus, double DspMillionSamplesPerSecond,
    SetupDependency[] Dependencies, string[] Warnings);
public sealed record SetupRecommendation(string ModelId, string Reason);
public sealed record SetupDownloadPlan(string ModelId, string Language, ModelDownload[] Files, bool Cuda, long RequiredModelBytes)
{
    public long DownloadBytes => Files.Sum(f => f.Size) + (Cuda ? SherpaRuntimes.CudaDownloads.Sum(f => f.Size) : 0);
}
public sealed record SetupState(int Version, string Outcome, DateTimeOffset CompletedAt, SetupReport? Report);

/// <summary>Local capability estimate; DSP throughput is never presented as an ASR or GPU benchmark.</summary>
public static class FirstRunSetup
{
    public const int Version = 1;
    public static bool NeedsSetup(string root)
    {
        try { return Json.Read<SetupState>(File.ReadAllText(Path.Combine(root, "setup-state.json"))).Version < Version; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return true; }
    }
    public static void Complete(string root, string outcome, SetupReport? report)
    {
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "setup-state.json"); var temporary = target + ".tmp";
        File.WriteAllText(temporary, Json.Write(new SetupState(Version, outcome, DateTimeOffset.UtcNow, report)));
        File.Move(temporary, target, true);
    }
    public static async Task<SetupReport> InspectInWorkerAsync(AppPaths paths, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var result = await Processes.RunAsync(WorkerClient.FindExecutable(paths), ["--setup-probe", paths.EngineRoot, paths.ModelRoot, paths.DataRoot], ct: timeout.Token);
        Processes.EnsureSuccess(result, "检测电脑配置");
        return Json.Read<SetupReport>(result.Output);
    }
    public static async Task<SetupReport> InspectAsync(AppPaths paths, CancellationToken ct)
    {
        var warnings = new List<string>(); var cpu = "处理器信息不可用"; ulong total = 0, available = 0;
        var gpus = Array.Empty<SetupGpu>(); long free = 0;
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? cpu;
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref memory)) { total = memory.TotalPhysical; available = memory.AvailablePhysical; }
            else warnings.Add("无法读取物理内存，采用保守建议。");
            try { gpus = ReadGpus(); }
            catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException) { warnings.Add("显卡检测不可用：" + ex.Message); }
        }
        try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(paths.ModelRoot))!).AvailableFreeSpace; }
        catch (IOException) { warnings.Add("无法读取模型磁盘空间。"); }
        var throughput = DspBenchmark(ct);
        var dependencies = new List<SetupDependency>();
        async Task Check(string name, string id, string file, string[]? args = null)
        {
            try
            {
                var executable = paths.FindEngine(id, file);
                if (args is not null)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    var result = await Processes.RunAsync(executable, args, ct: deadline.Token);
                    Processes.EnsureSuccess(result, name);
                }
                dependencies.Add(new(name, true, "已随安装包提供 · " + AppPaths.Rid));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { dependencies.Add(new(name, false, "启动检查超时，请修复安装")); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or BadImageFormatException)
            { dependencies.Add(new(name, false, ex.Message)); }
        }
        await Check("音视频解码", "ffmpeg", "ffmpeg.exe", ["-version"]);
        await Check("Whisper 兼容引擎", "whisper-cpu", "whisper-cli.exe", ["--help"]);
        await Check("Whisper Vulkan 引擎", "whisper-vulkan", "whisper-cli.exe");
        await Check("Whisper CUDA 引擎", "whisper-cuda", "whisper-cli.exe");
        dependencies.Add(new("ONNX DirectML 运行时", SherpaRuntimes.Find(paths, "directml") is not null, "随安装包提供；实际 GPU 执行由转写任务验证"));
        return new(cpu, RuntimeInformation.OSArchitecture.ToString(), Environment.ProcessorCount, total, available, free, gpus, throughput, dependencies.ToArray(), warnings.ToArray());
    }
    public static SetupRecommendation Recommend(SetupReport report, string language)
    {
        const ulong gb = 1024UL * 1024 * 1024;
        if (report.MemoryBytes < 6 * gb || report.AvailableMemoryBytes < 2 * gb)
            return new("whisper-tiny", "当前可用内存较少，先用轻量模型，之后可在模型商店升级。");
        var roomyGpu = report.Gpus.Any(g => g.DedicatedBytes >= 8 * gb || g.SharedBytes >= 8 * gb && report.AvailableMemoryBytes >= 16 * gb);
        if (report.MemoryBytes >= 24 * gb && report.AvailableMemoryBytes >= 12 * gb && roomyGpu
            && report.LogicalCores >= 8 && report.DspMillionSamplesPerSecond >= 500)
            return new("whisper-large", "内存与显卡容量充足，优先选择完整 large-v3，以识别精度为先；实际转写速度由任务验证。");
        if (language == "en" && report.MemoryBytes >= 8 * gb && report.Dependencies.Any(d => d.Name == "ONNX DirectML 运行时" && d.Ready))
            return new("parakeet-v3", "英语素材可选择 Parakeet v3；使用 CUDA / DirectML，不支持中文。");
        var gpuMemory = report.Gpus.Any(g => g.DedicatedBytes >= 3 * gb || g.SharedBytes >= 4 * gb && report.AvailableMemoryBytes >= 8 * gb);
        if (report.MemoryBytes >= 12 * gb && report.AvailableMemoryBytes >= 5 * gb && gpuMemory)
            return new("whisper-turbo", "内存与显卡容量适合 large-v3-turbo，兼顾多语言识别与速度；自动尝试 GPU。");
        if (report.DspMillionSamplesPerSecond > 0 && report.DspMillionSamplesPerSecond < 120 && report.Gpus.Length == 0)
            return new("whisper-tiny", "本次 CPU 运算测试较慢且未检测到硬件显卡，先用 tiny 减少等待。");
        return new("whisper-small", "优先控制内存与处理耗时；small 支持多语言，后续可更换更大的模型。");
    }
    public static SetupDownloadPlan Plan(string modelId, string language, bool cuda)
    {
        var model = StoreModels.All.Single(m => m.Id == modelId && m.Category == "音视频转写");
        if (language is not ("auto" or "en" or "zh") || model.Asr is not null && language != "auto" && !model.Asr.LanguageCodes.Contains(language))
            throw new ArgumentException("所选模型不支持素材语言。");
        var files = new List<ModelDownload> { model.Asr?.Download ?? model.Download ?? throw new ArgumentException("模型不可下载") };
        if (model.Asr is null) files.Add(ModelCatalog.Items.Single(m => m.Id == "vad"));
        return new(modelId, language, files.ToArray(), cuda && model.Asr is not null, files.Sum(f => f.Size) * (model.Asr is null ? 1 : 4) + 512L * 1024 * 1024);
    }
    public static async Task<AppSettings> InstallAsync(AppPaths paths, AppSettings original, SetupDownloadPlan plan, IProgress<(double Fraction, string Message)>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var model = StoreModels.All.Single(m => m.Id == plan.ModelId);
        var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(paths.ModelRoot))!).AvailableFreeSpace;
        if (free < plan.RequiredModelBytes) throw new IOException("模型磁盘空间不足，请释放空间或在设置中更换模型目录。");
        for (var i = 0; i < plan.Files.Length; i++)
        {
            var file = plan.Files[i]; var index = i; var steps = plan.Files.Length + (plan.Cuda ? 1 : 0);
            var itemProgress = new Progress<double>(p => progress?.Report(((index + p) / steps, $"{file.Name} · {p:P0} · 下载 / 校验")));
            if (model.Asr is not null && i == 0) await AsrModels.InstallAsync(model.Asr, paths.ModelRoot, itemProgress, ct);
            else
            {
                using var downloader = new ModelDownloader();
                await downloader.DownloadAsync(file, new(file.Size, file.Sha256), paths.ModelRoot, itemProgress, ct);
            }
        }
        if (plan.Cuda)
            await SherpaRuntimes.InstallCudaAsync(paths, new Progress<double>(p => progress?.Report(((plan.Files.Length + p) / (plan.Files.Length + 1), $"CUDA 运行时 · {p:P0} · 下载 / 校验 / 解压"))), ct);
        ct.ThrowIfCancellationRequested();
        // Commit settings only after every selected component is complete. Cancellation retains the old settings.
        var result = Json.Read<AppSettings>(Json.Write(original));
        StoreModels.Apply(model, result, paths.ModelRoot); result.Backend = ComputeBackend.Auto; result.Language = plan.Language;
        if (model.Asr is null) StoreModels.Apply(StoreModels.All.Single(m => m.Id == "vad"), result, paths.ModelRoot);
        progress?.Report((1, "模型与所需组件已就绪")); return result;
    }
    private static double DspBenchmark(CancellationToken ct)
    {
        var data = new float[65536]; for (var i = 0; i < data.Length; i++) data[i] = (i % 100 - 50) / 50f;
        double checksum = 0; long samples = 0; var watch = Stopwatch.StartNew();
        do
        {
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i <= data.Length - Vector<float>.Count; i += Vector<float>.Count)
            { var vector = new Vector<float>(data, i); checksum += Vector.Dot(vector, vector); }
            samples += data.Length;
        } while (watch.Elapsed.TotalSeconds < .75);
        GC.KeepAlive(checksum); return samples / watch.Elapsed.TotalSeconds / 1_000_000;
    }
    // DXGI's documented COM layout; shared memory is a limit, never added to physical RAM or dedicated VRAM.
    private static SetupGpu[] ReadGpus()
    {
        var iid = new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387"); Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        try
        {
            var enumerate = Marshal.GetDelegateForFunctionPointer<EnumAdapter>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 12 * IntPtr.Size));
            var result = new List<SetupGpu>();
            for (uint index = 0; index < 32; index++)
            {
                var hr = enumerate(factory, index, out var adapter); if (hr == unchecked((int)0x887A0002)) break; Marshal.ThrowExceptionForHR(hr);
                try
                {
                    var describe = Marshal.GetDelegateForFunctionPointer<GetDescription>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 10 * IntPtr.Size));
                    Marshal.ThrowExceptionForHR(describe(adapter, out var description));
                    if ((description.Flags & 2) == 0) result.Add(new(description.Description, description.Vendor, (ulong)description.DedicatedVideo, (ulong)description.Shared));
                }
                finally { Marshal.Release(adapter); }
            }
            return result.ToArray();
        }
        finally { Marshal.Release(factory); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Vendor, Device, Subsystem, Revision;
        public nuint DedicatedVideo, DedicatedSystem, Shared;
        public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapter(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr self, out AdapterDescription description);
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}
