using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MemoAI.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidDataException("数据为空或格式不正确。");
}

public enum ComputeBackend { Auto, Cuda, Vulkan, Cpu, DirectML }
public enum JobState { Queued, Running, Completed, Cancelled, Failed, Interrupted }
public enum LanguageModelKind { Ollama, BuiltIn }
public enum SubtitleTextMode { Bilingual, Original, Translation }
public sealed record TimedWord(string Text, double Start, double End);

public partial class SubtitleSegment : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TimeLabel))] private double start;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TimeLabel))] private double end;
    [ObservableProperty] private string text = "";
    [ObservableProperty] private string translation = "";
    [ObservableProperty] private string speaker = "";
    public List<TimedWord> Words { get; set; } = [];
    [JsonIgnore] public string TimeLabel => $"{TimeSpan.FromSeconds(Math.Max(0, Start)):hh\\:mm\\:ss} → {TimeSpan.FromSeconds(Math.Max(0, End)):hh\\:mm\\:ss}";
    public SubtitleSegment Copy() => new() { Id = Id, Start = Start, End = End, Text = Text, Translation = Translation, Speaker = Speaker, Words = [..Words] };
}

public partial class MediaSession : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string title = "未命名媒体";
    public string MediaPath { get; set; } = "";
    public string RecordingPath { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string PlaybackPath { get; set; } = "";
    [ObservableProperty] private string folder = "收件箱";
    [ObservableProperty] private string summary = "";
    public string MindMapJson { get; set; } = "";
    public string Chapters { get; set; } = "";
    public double Duration { get; set; }
    public TranscriptionOptions? Transcription { get; set; }
    public ObservableCollection<ChatTurn> Chat { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public ObservableCollection<SubtitleSegment> Segments { get; set; } = [];
    [JsonIgnore] public string Description => $"{CreatedAt:MM-dd HH:mm}  ·  {Segments.Count} 条字幕  ·  {Folder}";
    [JsonIgnore] public string DurationLabel => Duration > 0 ? TimeSpan.FromSeconds(Duration).ToString(@"hh\:mm\:ss") : "音视频 / 字幕";
}

public partial class ChatTurn : ObservableObject
{
    public string Role { get; set; } = "assistant";
    [ObservableProperty] private string content = "";
    [ObservableProperty] private string status = "";
    public string Label => Role == "user" ? "你" : "WinWhisper";
}

public sealed class AppSettings
{
    public ComputeBackend Backend { get; set; } = ComputeBackend.Auto;
    public string TranscriptionModelPath { get; set; } = "";
    public string Language { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "简体中文";
    public bool AutoSegmentEnglish { get; set; } = true;
    public bool RemoveEnglishFillers { get; set; } = true;
    public bool CloudTranslation { get; set; }
    public string TranslationApiBase { get; set; } = "";
    public string TranslationApiModel { get; set; } = "";
    public LanguageModelKind ModelProvider { get; set; } = LanguageModelKind.Ollama;
    public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";
    public string TranslationModel { get; set; } = "hf.co/IndexTeam/Index-Translate-9B-GGUF:Q4_K_M";
    public string SummaryModel { get; set; } = "qwen3.5:35b-a3b";
    public string GgufPath { get; set; } = "";
    public string EngineRoot { get; set; } = "";
    public string ModelRoot { get; set; } = "";
    public string Theme { get; set; } = "Default";
    public bool ReduceMotion { get; set; }
    public bool UseMica { get; set; }
    public string LibraryViewMode { get; set; } = "List";
    public bool EnableVad { get; set; } = true;
    public string VadModelPath { get; set; } = "";
    public string Glossary { get; set; } = "";
    public int TranscriptionThreads { get; set; }
    public int TranscriptionBeamSize { get; set; } = 5;
    public int TranscriptionChunkSeconds { get; set; } = 300;
    public int TranscriptionMaxLength { get; set; }
    public double TranscriptionTemperature { get; set; }
    public string SummaryPrompt { get; set; } = "用中文总结以下内容，包含主题概述、关键观点、事实与结论。保留人名、产品名、数字；不要编造原文不存在的信息。";
    public string ObsidianFolder { get; set; } = "";
    public string NotionParentPage { get; set; } = "";
    public string TtsModelPath { get; set; } = "";
    public string TtsVoicesPath { get; set; } = "";
    public string TtsTokensPath { get; set; } = "";
    public string TtsDataDirectory { get; set; } = "";
    public string TtsLexiconPath { get; set; } = "";
    public int TtsVoice { get; set; } = 50;
    public string SpeakerSegmentationModel { get; set; } = "";
    public string SpeakerEmbeddingModel { get; set; } = "";
}

public sealed class TranscriptionOptions
{
    public string ModelPath { get; set; } = "";
    public string Language { get; set; } = "auto";
    public ComputeBackend Backend { get; set; }
    public bool EnableVad { get; set; } = true;
    public bool RemoveEnglishFillers { get; set; } = true;
    public int Threads { get; set; }
    public int BeamSize { get; set; } = 5;
    public int ChunkSeconds { get; set; } = 300;
    public int MaxLength { get; set; }
    public double Temperature { get; set; }
    public string Prompt { get; set; } = "";
    public static TranscriptionOptions FromSettings(AppSettings s) => new()
    {
        ModelPath = s.TranscriptionModelPath, Language = s.Language, Backend = s.Backend, EnableVad = s.EnableVad, RemoveEnglishFillers = s.RemoveEnglishFillers,
        Threads = s.TranscriptionThreads, BeamSize = s.TranscriptionBeamSize, ChunkSeconds = s.TranscriptionChunkSeconds,
        MaxLength = s.TranscriptionMaxLength, Temperature = s.TranscriptionTemperature, Prompt = s.Glossary
    };
    public AppSettings ApplyTo(AppSettings defaults)
    {
        if (Threads is < 0 or > 64 || BeamSize is < 1 or > 10 || ChunkSeconds is < 30 or > 900 || MaxLength is < 0 or > 200 || !double.IsFinite(Temperature) || Temperature is < 0 or > 1)
            throw new ArgumentException("转写参数超出允许范围。");
        var s = Json.Read<AppSettings>(Json.Write(defaults));
        s.TranscriptionModelPath = ModelPath; s.Language = Language; s.Backend = Backend; s.EnableVad = EnableVad; s.RemoveEnglishFillers = RemoveEnglishFillers;
        s.TranscriptionThreads = Threads; s.TranscriptionBeamSize = BeamSize; s.TranscriptionChunkSeconds = ChunkSeconds;
        s.TranscriptionMaxLength = MaxLength; s.TranscriptionTemperature = Temperature; s.Glossary = Prompt;
        return s;
    }
}

public sealed record JobRequest(string Id, string SessionId, string Operation, string MediaPath,
    string WorkDirectory, AppSettings Settings, double? RangeStart = null, double? RangeEnd = null,
    string? Text = null, string? OutputPath = null, SubtitleSegment[]? InputSegments = null);

public sealed record JobEvent(string Type, string Message = "", double Progress = 0,
    SubtitleSegment[]? Segments = null, string? Backend = null, string? Device = null,
    double? ElapsedSeconds = null, double? RealTimeFactor = null, string? Path = null);

public sealed class JobRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Operation { get; set; } = "转写";
    public JobState State { get; set; } = JobState.Queued;
    public double Progress { get; set; }
    public string Message { get; set; } = "等待处理";
    public string Backend { get; set; } = "";
    public string Device { get; set; } = "";
    public double? ElapsedSeconds { get; set; }
    public double? RealTimeFactor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public JobRequest? Request { get; set; }
    [JsonIgnore] public string Display => $"{Title} · {State switch { JobState.Queued => "等待中", JobState.Running => "处理中", JobState.Completed => "已完成", JobState.Cancelled => "已取消", JobState.Failed => "失败", _ => "已中断" }} · {Progress:P0}  {Message}";
}

public sealed class AppPaths
{
    public string DataRoot { get; }
    public string EngineRoot { get; set; }
    public string ModelRoot { get; set; }
    public static string Rid => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
    public AppPaths(string? dataRoot = null, string? engineRoot = null)
    {
        DataRoot = dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoAI.Native");
        var packageRoot = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(packageRoot);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "engines"))) dir = dir.Parent;
        EngineRoot = engineRoot ?? Path.Combine(dir?.FullName ?? packageRoot, "engines");
        ModelRoot = Path.Combine(DataRoot, "models");
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(ModelRoot);
    }
    public string SessionDirectory(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("无效的媒体 ID");
        var path = Path.Combine(DataRoot, "sessions", id);
        Directory.CreateDirectory(path);
        return path;
    }
    public string FindEngine(string id, string file)
    {
        var toolRid = id is "ffmpeg" && Environment.GetEnvironmentVariable("WINWHISPER_MEDIA_RID") is "win-arm64" or "win-x64" ? Environment.GetEnvironmentVariable("WINWHISPER_MEDIA_RID")! : Rid;
        var architectureRoot = Path.Combine(EngineRoot, toolRid, id);
        // Legacy layout is allowed only after validating each executable's machine type.
        var root = Directory.Exists(architectureRoot) ? architectureRoot : Path.Combine(EngineRoot, id);
        var found = Directory.Exists(root) ? Directory.EnumerateFiles(root, file, SearchOption.AllDirectories).FirstOrDefault() : null;
        if (found is null && id == "yt-dlp" && Directory.Exists(root))
            found = Directory.EnumerateFiles(root, Rid == "win-arm64" ? "yt-dlp_arm64.exe" : "yt-dlp.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (found is null) throw new FileNotFoundException($"缺少 {Rid} 的 {id} 引擎。请运行 scripts/fetch-engines.ps1 或在设置中选择引擎目录。", file);
        PeArchitecture.RequireCompatible(found, toolRid);
        return found;
    }
}

public static class PeArchitecture
{
    public static ushort ReadMachine(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0x5A4D) throw new InvalidDataException($"不是 PE 文件：{path}");
        stream.Position = 0x3C;
        var offset = reader.ReadInt32();
        if (offset < 0 || offset > stream.Length - 6) throw new InvalidDataException("PE 文件损坏");
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x4550) throw new InvalidDataException("PE 签名无效");
        return reader.ReadUInt16();
    }
    public static void RequireCompatible(string path, string? rid = null)
    {
        rid ??= AppPaths.Rid;
        if (rid is not ("win-arm64" or "win-x64")) throw new ArgumentException("未知的引擎架构。");
        var expected = rid == "win-arm64" ? (ushort)0xAA64 : (ushort)0x8664;
        if (ReadMachine(path) != expected) throw new InvalidDataException($"引擎架构不匹配（需要 {rid}）：{path}");
    }
}

public interface ITranscriptionEngine
{
    Task TranscribeAsync(JobRequest request, Func<JobEvent, Task> progress, CancellationToken ct);
}
public interface ILocalLanguageModelProvider
{
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);
    Task<string> GenerateAsync(string model, string system, string prompt, Func<string, Task>? onDelta, CancellationToken ct);
}
public interface ISpeechSynthesizer
{
    Task SynthesizeAsync(JobRequest request, Func<JobEvent, Task> progress, CancellationToken ct);
}
public interface IJobScheduler
{
    Task<JobRecord> EnqueueAsync(MediaSession session, string operation, CancellationToken ct = default);
    void Cancel(string jobId);
}
