using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using MemoAI.Core;

var root = args.FirstOrDefault(a => a.StartsWith("--root="))?[7..] ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var run = Path.Combine(root, "artifacts", "tests", DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(run);
var results = new List<object>(); var failures = 0;
async Task Test(string name, Func<Task> action)
{
    var watch = Stopwatch.StartNew();
    try { await action(); results.Add(new { name, passed = true, seconds = watch.Elapsed.TotalSeconds }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + "\n" + ex); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }
Task Sync(Action action) { action(); return Task.CompletedTask; }
var session = new MediaSession
{
    Title = "中文路径与双语字幕",
    Duration = 12,
    Segments = new ObservableCollection<SubtitleSegment>
    {
        new() { Start = 0.123, End = 4.567, Text = "Hello, world.", Translation = "你好，世界。" },
        new() { Start = 5.001, End = 11.999, Text = "Windows ARM64 and x64.", Translation = "双平台测试。" }
    }
};
await Test("SRT/VTT bilingual round-trip and millisecond timestamps", () => Sync(() =>
{
    foreach (var format in new[] { "srt", "vtt" })
    {
        var actual = Subtitles.Parse(Subtitles.Export(session, format), bilingual: true);
        Check(actual.Count == 2 && actual[0].Text == "Hello, world." && actual[0].Translation == "你好，世界。", "双语文本丢失");
        Check(Math.Abs(actual[1].End - 11.999) < 0.0001, "毫秒时间戳改变");
    }
}));
await Test("VTT cue settings, BOM, CRLF and invalid time rejection", () => Sync(() =>
{
    var cues = Subtitles.Parse("\uFEFFWEBVTT\r\n\r\n00:01.250 --> 00:02.750 align:start\r\n测试中文\r\n\r\n00:04.000 --> 00:03.000\r\ninvalid\r\n");
    Check(cues.Count == 1 && cues[0].Start == 1.25 && cues[0].Text == "测试中文", "VTT 解析不正确");
}));
await Test("Subtitle repair keeps valid content and removes adjacent duplicates", () => Sync(() =>
{
    var repaired = Subtitles.Repair([new() { Start = 0, End = 1, Text = "Hello" }, new() { Start = 1, End = 2, Text = "Hello" }, new() { Start = 1.8, End = 3, Text = "World" }, new() { Start = 3, End = 4, Text = " " }]);
    Check(repaired.Count == 2 && repaired[0].End == 2 && repaired[1].Start == 2, "重复合并或重叠修复错误");
}));
await Test("Split and merge preserve source and translated characters", () => Sync(() =>
{
    var s = session.Segments[0]; var (a, b) = Subtitles.Split(s, 2);
    Check(a.Text + b.Text == s.Text && a.Translation + b.Translation == s.Translation && a.End == b.Start, "拆分丢失字符");
    var merged = Subtitles.Merge(a, b); Check(merged.Start == s.Start && merged.End == s.End, "合并时间错误");
}));
await Test("ASS escaping and SVG XML encoding", () => Sync(() =>
{
    var malicious = new MediaSession { Title = "<Title>", Segments = [new() { Start = 0, End = 1, Text = "{\\pos(0,0)} hello\nworld" }] };
    var ass = Subtitles.Export(malicious, "ass"); Check(ass.Contains("\\{") && ass.Contains("\\N"), "ASS 字幕未转义");
    var svg = Subtitles.MindMapSvg("{\"title\":\"<script>&\",\"children\":[{\"title\":\"子节点\",\"children\":[]}]}", "<Title>");
    _ = System.Xml.Linq.XDocument.Parse(svg); Check(!svg.Contains("<script>"), "SVG 内容未转义");
}));
await Test("SQLite persistence, search, folders, jobs and settings survive restart", () => Sync(() =>
{
    var data = Path.Combine(run, "db"); var db = new Store(data); session.Folder = "播客"; db.SaveSession(session);
    var job = new JobRecord { SessionId = session.Id, Title = session.Title, State = JobState.Interrupted }; db.SaveJob(job);
    db.SaveSettings(new() { Backend = ComputeBackend.Cuda, Glossary = "AI = 人工智能" });
    var restart = new Store(data); Check(restart.GetSession(session.Id)!.Segments[0].Translation == "你好，世界。", "字幕未持久化");
    Check(restart.GetSessions("Windows", "播客").Count == 1 && restart.GetSessions("Windows", "其他").Count == 0, "搜索或文件夹过滤错误");
    Check(restart.GetJobs().Single().State == JobState.Interrupted && restart.LoadSettings().Backend == ComputeBackend.Cuda, "任务或设置未持久化");
}));
await Test("Chinese typography preserves code, links and URLs", () => Sync(() =>
{
    var text = "他说“使用GPT6整理3条建议，阅读《三体》”。\n```cs\n中文Code3=1;\n```\n`中文Code3` https://example.com/中文Code3 [链接](https://example.com/中文Code3)";
    var actual = ChineseTypography.Apply(text);
    Check(actual.StartsWith("他说「使用 GPT6 整理 3 条建议，阅读《三体》」。"), "中文引号或中英数间距未规范");
    Check(actual.Contains("```cs\n中文Code3=1;\n```") && actual.Contains("`中文Code3`") && actual.Contains("https://example.com/中文Code3"), "代码或网址被改写");
    Check(ChineseTypography.Apply(actual) == actual, "格式规范化不是幂等操作");
    Check(ChineseTypography.Apply("大家好,欢迎使用AI,共有1,000条记录。 English, sentence.") == "大家好，欢迎使用 AI，共有 1,000 条记录。 English, sentence.", "中文逗号或数字分隔符处理错误");
    Check(ChineseTypography.Apply("过去**12个月**使用**AI**处理。") == "过去 **12 个月**使用 **AI** 处理。", "Markdown 强调两侧中英数字间距错误");
    Check(ChineseTypography.Apply("It’s an English sentence, with “quotes”.") == "It’s an English sentence, with “quotes”.", "英文撇号或引号被改写");
    Check(ChineseTypography.Apply("{\"title\":\"中文Code3,内容\"}") == "{\"title\":\"中文Code3,内容\"}", "有效 JSON 被破坏");
}));
await Test("Chat history persists and follow-up includes previous turns", async () =>
{
    var media = new MediaSession { Segments = [new() { Start = 0, End = 5, Text = "这个节目讨论本地 AI。" }], Chat = [new() { Role = "user", Content = "主要话题是什么？" }, new() { Role = "assistant", Content = "本地 AI。" }] };
    var db = new Store(Path.Combine(run, "chat-history")); db.SaveSession(media);
    var restored = db.GetSession(media.Id)!; var model = new FakeModel();
    await new AiService(model, new()).AskAsync(restored, "它的优势是什么？", null, CancellationToken.None, restored.Chat.ToArray());
    Check(model.LastPrompt.Contains("主要话题是什么") && model.LastPrompt.Contains("本地 AI") && model.LastSystem.Contains("直角引号"), "对话历史或默认中文规范未进入请求");
});
await Test("Per-media transcription settings persist and snapshot independently", () => Sync(() =>
{
    var defaults = new AppSettings { TranscriptionModelPath = "global.bin", Language = "auto" };
    var media = new MediaSession { Transcription = new() { ModelPath = "session.bin", Language = "zh", Backend = ComputeBackend.Cuda, Threads = 4, BeamSize = 2, ChunkSeconds = 60, MaxLength = 35, Temperature = 0.1, Prompt = "专有名词" } };
    var data = new Store(Path.Combine(run, "transcription-options")); data.SaveSession(media);
    var options = data.GetSession(media.Id)!.Transcription!; var snapshot = options.ApplyTo(defaults);
    options.ModelPath = "next.bin"; options.Language = "en";
    Check(snapshot.TranscriptionModelPath == "session.bin" && snapshot.Language == "zh" && snapshot.TranscriptionChunkSeconds == 60 && snapshot.TranscriptionMaxLength == 35 && snapshot.TranscriptionBeamSize == 2 && snapshot.Glossary == "专有名词", "配置快照被后续编辑改变");
    Check(defaults.Language == "auto" && defaults.TranscriptionModelPath == "global.bin", "媒体配置污染了全局默认值");
    options.ChunkSeconds = 0; try { options.ApplyTo(defaults); throw new Exception("无效分块参数未拒绝"); } catch (ArgumentException) { }
}));
await Test("Online download browser selection is explicit and secrets are not logged", () => Sync(() =>
{
    var plain = OnlineDownloads.Arguments("https://www.youtube.com/watch?v=fixture", "output", "ffmpeg", "deno.exe", new());
    Check(!plain.Contains("--cookies-from-browser") && plain.Contains("--no-cookies-from-browser") && plain.Contains("--ignore-config"), "默认读取了浏览器或外部配置");
    var authenticated = OnlineDownloads.Arguments("https://www.youtube.com/watch?v=fixture", "output", "ffmpeg", "deno.exe", new("edge", "Profile 1", Proxy: "http://127.0.0.1:7897", Quality: 3));
    Check(authenticated.Contains("edge:Profile 1") && authenticated.Contains("ba/b") && authenticated[^2] == "--", "浏览器资料或参数边界错误");
    Check(OnlineDownloads.ProgressMessage("Cookie: secret-value") == "正在读取选定的登录状态…" && !OnlineDownloads.FailureMessage("DPAPI secret-value").Contains("secret-value"), "登录信息泄漏到显示消息");
    try { OnlineDownloads.Arguments("https://example.com", "output", "ffmpeg", "deno.exe", new("unsupported")); throw new Exception("未知浏览器未拒绝"); } catch (ArgumentException) { }
}));
await Test("Long transcript chunks preserve all cues within context budget", () => Sync(() =>
{
    var cues = Enumerable.Range(0, 10000).Select(i => new SubtitleSegment { Start = i, End = i + 0.9, Text = "中文 English segment " + i }).ToArray();
    var chunks = AiService.ChunkTranscript(cues, 14000).ToArray(); Check(chunks.Length > 10 && chunks.All(c => c.Length <= 14000), "上下文块超限");
    var content = string.Concat(chunks); Check(cues.All(c => content.Contains(c.Text + "\n")), "长字幕被截断");
    var giant = AiService.ChunkTranscript([new() { Start = 0, End = 1, Text = new string('中', 30000) }], 14000).ToArray();
    Check(giant.All(c => c.Length <= 14000) && string.Concat(giant).Count(c => c == '中') == 30000, "单条超长字幕被丢弃");
}));
await Test("Local model endpoint rejects external destinations", async () =>
{
    using var provider = new LocalLanguageModelProvider(new AppPaths(Path.Combine(run, "local-only")), new() { OllamaEndpoint = "https://example.com" });
    try { await provider.ListModelsAsync(CancellationToken.None); throw new Exception("外部地址没有被阻止"); }
    catch (ArgumentException) { }
});
await Test("AI translation checkpoints and cancellation retain finished subtitles", async () =>
{
    var fake = new FakeModel(); var ai = new AiService(fake, new()); var cues = session.Segments.Select(s => s.Copy()).ToArray();
    var checkpoints = 0; using var stop = new CancellationTokenSource();
    try { await ai.TranslateAsync(cues, _ => { checkpoints++; stop.Cancel(); return Task.CompletedTask; }, stop.Token); }
    catch (OperationCanceledException) { }
    Check(checkpoints == 1 && cues[0].Translation == "译文" && fake.Calls == 1, "取消后继续覆盖后续字幕");
});
await Test("Session work path refuses traversal IDs", () => Sync(() =>
{
    var paths = new AppPaths(Path.Combine(run, "path-validation"));
    try { paths.SessionDirectory("../outside"); throw new Exception("路径穿越未阻止"); } catch (ArgumentException) { }
}));
await Test("Standard multiline SRT remains original text", () => Sync(() =>
{
    var parsed = Subtitles.Parse("1\n00:00:00,000 --> 00:00:02,000\nThe first line\nand the second line\n");
    Check(parsed.Single().Text == "The first line\nand the second line" && parsed[0].Translation == "", "普通多行字幕被误判为译文");
}));
await Test("Model store applies only the selected model capability", () => Sync(() =>
{
    var directory = Path.Combine(run, "model-store"); Directory.CreateDirectory(directory);
    File.WriteAllBytes(Path.Combine(directory, "fixture.bin"), [1, 2, 3, 4]);
    var entry = new StoreModel("whisper-fixture", "test", "音视频转写", "", "", "4 bytes", "", "", new("fixture", "fixture", "test", "fixture.bin", "", 4));
    var options = new AppSettings { SummaryModel = "keep-summary", TranslationModel = "keep-translation" };
    Check(StoreModels.IsInstalled(entry, options, directory) && !StoreModels.IsApplied(entry, options, directory), "模型状态识别错误");
    StoreModels.Apply(entry, options, directory);
    Check(StoreModels.IsApplied(entry, options, directory) && options.SummaryModel == "keep-summary" && options.TranslationModel == "keep-translation", "应用转写模型意外修改语言模型");
    Check(ModelCatalog.Items.All(m => m.Size > 0 && m.Sha256.Length == 64), "商店模型缺少固定大小或校验值");
}));
await Test("Model download resumes via HTTP Range and verifies SHA256", async () =>
{
    var data = Enumerable.Range(0, 300000).Select(i => (byte)(i % 251)).ToArray();
    var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
    var directory = Path.Combine(run, "download-resume"); Directory.CreateDirectory(directory);
    await File.WriteAllBytesAsync(Path.Combine(directory, "fixture.bin.partial"), data[..100000]);
    var handler = new DownloadHandler(data); using var downloader = new ModelDownloader(handler);
    var file = await downloader.DownloadAsync(new("fixture", "test", "repo", "fixture.bin", ""), new(data.Length, sha), directory, null, CancellationToken.None);
    Check(handler.RangeStart == 100000 && (await File.ReadAllBytesAsync(file)).SequenceEqual(data), "断点续传内容不正确");
    Check(!File.Exists(file + ".partial"), "完成后残留半成品");
});
await Test("Corrupt model is rejected and never applied", async () =>
{
    var data = new byte[] { 1, 2, 3, 4 }; var directory = Path.Combine(run, "corrupt-download");
    using var downloader = new ModelDownloader(new DownloadHandler(data));
    try { await downloader.DownloadAsync(new("fixture", "test", "repo", "fixture.bin", ""), new(4, new string('0', 64)), directory, null, CancellationToken.None); throw new Exception("损坏模型被接受"); }
    catch (InvalidDataException) { }
    Check(!File.Exists(Path.Combine(directory, "fixture.bin")), "错误模型被移动到正式路径");
});

await Test("ASR catalog has pinned assets, languages and engine dispatch", () => Sync(() =>
{
    Check(AsrModels.All.Length >= 10 && AsrModels.All.Select(m => m.Id).Distinct().Count() == AsrModels.All.Length, "模型目录缺失或 ID 重复");
    foreach (var m in AsrModels.All)
    {
        Check(m.Bytes > 0 && m.Sha256.Length == 64 && m.LanguageCodes.Length > 0, "模型未固定大小、校验值或语言");
        Check(StoreModels.All.Any(s => s.Asr == m), "ASR 模型未在商店显示");
    }
    Check(!AsrModels.All.First(m => m.Id == "parakeet-v3").LanguageCodes.Contains("zh"), "Parakeet 错误宣称支持中文");
    var paths = new AppPaths(Path.Combine(run, "asr-dispatch"));
    Check(TranscriptionEngines.Create(paths, "model.asr.json") is SherpaTranscription && TranscriptionEngines.Create(paths, "model.bin") is WhisperEngine, "转写引擎未按模型切换");
    Check(!AsrModels.IsInstalled(AsrModels.All[0], paths.ModelRoot), "不存在的模型被标记为已安装");
    try { AsrModels.SafePath(paths.ModelRoot, "../escape.onnx"); throw new Exception("包路径穿越被接受"); } catch (InvalidDataException) { }
    Check(SherpaTranscription.AudioRanges(new float[16000], true).Count == 0, "静音被发送给识别器");
    Check(SherpaTranscription.AudioRanges(new float[16000], false).Single() == (0, 16000), "关闭静音过滤后音频被截断");
}));
var asrTest = args.FirstOrDefault(a => a.StartsWith("--asr="))?[6..];
await Test("ASR installation stays atomic across replacement and rejects missing/traversing files", () => Sync(() =>
{
    var model = AsrModels.All.First(m => m.Id == "sensevoice-int8"); var modelRoot = Path.Combine(run, "asr-replace");
    var manifest = model.ManifestPath(modelRoot); var installRoot = Path.GetDirectoryName(manifest)!;
    foreach (var version in new[] { "content-old", "content-current" })
    {
        var directory = Path.Combine(installRoot, version); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "model.int8.onnx"), [1, 2, 3, 4]); File.WriteAllText(Path.Combine(directory, "tokens.txt"), "fixture");
    }
    var installed = new AsrInstallation(1, model.Id, model.Sha256,
        [new("content-current/model.int8.onnx", 4), new("content-current/tokens.txt", new FileInfo(Path.Combine(installRoot, "content-current", "tokens.txt")).Length)]);
    File.WriteAllText(manifest, Json.Write(installed));
    Check(AsrModels.IsInstalled(model, modelRoot) && AsrModels.ResolveFiles(manifest)["model"].Contains("content-current"), "旧模型目录干扰了新安装");
    File.WriteAllBytes(Path.Combine(installRoot, "content-current", "model.int8.onnx"), [1]);
    Check(!AsrModels.IsInstalled(model, modelRoot), "被截断的模型仍显示已安装");
    File.WriteAllText(manifest, Json.Write(installed with { Files = [new("../escape", 4)] }));
    Check(!AsrModels.IsInstalled(model, modelRoot), "安装记录中的越界路径被接受");
}));
if (asrTest is not null)
{
    var paths = new AppPaths(Path.Combine(run, "asr-data"), Path.Combine(root, "engines"));
    var modelRoot = Path.Combine(root, "artifacts", "test-assets", "asr-models");
    foreach (var id in asrTest.Split(','))
    {
        var model = AsrModels.All.First(m => m.Id == id);
        await Test("Real ASR install/configure/worker/recovery " + id, async () =>
        {
            if (!AsrModels.IsInstalled(model, modelRoot))
            {
                Console.WriteLine($"Downloading {model.Name} ({model.Bytes / 1048576} MB)");
                await AsrModels.InstallAsync(model, modelRoot, null, CancellationToken.None);
            }
            var options = new AppSettings { EngineRoot = paths.EngineRoot, Language = model.LanguageCodes.Contains("en") ? "en" : "zh", EnableVad = true };
            StoreModels.Apply(StoreModels.All.First(s => s.Id == id), options, modelRoot);
            Check(StoreModels.IsApplied(StoreModels.All.First(s => s.Id == id), options, modelRoot), "模型无法应用");
            var audio = Path.Combine(root, "artifacts", "test-assets", "jfk.wav");
            if (model.LanguageCodes.SequenceEqual(new[] { "zh" }))
                audio = Directory.GetFiles(Path.GetDirectoryName(model.ManifestPath(modelRoot))!, "*.wav", SearchOption.AllDirectories).First();
            var request = new JobRequest(Guid.NewGuid().ToString("N"), session.Id, "transcribe", audio, Path.Combine(run, id), options);
            JobEvent? final = null; var warnings = new List<string>();
            await new WorkerClient(paths).RunAsync(request, e => { if (e.Type == "completed") final = e; if (e.Type == "warning") warnings.Add(e.Message); return Task.CompletedTask; }, CancellationToken.None);
            Check(final?.Backend == "Cpu" && final.Segments?.Length > 0 && final.Segments.All(s => s.Start >= 0 && s.End > s.Start), "没有有效的 CPU 转写结果");
            if (options.Language == "en") Check(string.Join(" ", final!.Segments!.Select(s => s.Text)).Contains("country", StringComparison.OrdinalIgnoreCase), "真实音频未被正确识别");
            await File.WriteAllTextAsync(Path.Combine(run, id + "-result.json"), Json.Write(new { final, warnings }));
            var expected = string.Join(" ", final!.Segments!.Select(s => s.Text));
            await new WorkerClient(paths).RunAsync(request, e => { if (e.Type == "completed") final = e; return Task.CompletedTask; }, CancellationToken.None);
            Check(string.Join(" ", final!.Segments!.Select(s => s.Text)) == expected, "检查点恢复内容发生变化");
            var unsupported = Json.Read<AppSettings>(Json.Write(options)); unsupported.Backend = ComputeBackend.Cuda;
            try { SherpaTranscription.Configuration(unsupported.TranscriptionModelPath, unsupported); throw new Exception("错误报告 CUDA 支持"); } catch (NotSupportedException) { }
        });
    }
}

if (args.Contains("--live"))
{
    var paths = new AppPaths(Path.Combine(run, "live-data"), Path.Combine(root, "engines"));
    var model = AsrModels.All.First(m => m.Id == "sensevoice-int8");
    var options = new AppSettings { EngineRoot = paths.EngineRoot, TranscriptionModelPath = model.ManifestPath(Path.Combine(root, "artifacts", "test-assets", "asr-models")), Language = "en" };
    await Test("Live worker returns subtitles before recording ends, preserves offsets and completes", async () =>
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<LiveAudioClip>();
        var source = Path.Combine(root, "artifacts", "test-assets", "jfk.wav");
        var first = Path.Combine(run, "live-first.wav"); var second = Path.Combine(run, "live-second.wav");
        await new MediaTools(paths).ExtractAudioAsync(source, first, 0, 4, CancellationToken.None);
        await new MediaTools(paths).ExtractAudioAsync(source, second, 4, 4, CancellationToken.None);
        var receivedFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var segments = new List<SubtitleSegment>(); var completed = false;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var task = new WorkerClient(paths).RunLiveAsync(new(Guid.NewGuid().ToString("N"), session.Id, "transcribe-live", "", run, options), channel.Reader, e =>
        {
            if (e.Type == "segments" && e.Segments is { } cues) { segments.AddRange(cues); receivedFirst.TrySetResult(); }
            if (e.Type == "completed") completed = true;
            return Task.CompletedTask;
        }, stop.Token);
        await channel.Writer.WriteAsync(new(first, 0));
        await receivedFirst.Task.WaitAsync(TimeSpan.FromSeconds(45));
        Check(!task.IsCompleted, "实时字幕必须在音频流结束前返回");
        await channel.Writer.WriteAsync(new(second, 4)); channel.Writer.TryComplete(); await task;
        Check(completed && segments.Any(s => s.Start < 4) && segments.Any(s => s.Start >= 4) && segments.All(s => s.End <= 8.01), "实时字幕片段时间或结束状态错误");
        await File.WriteAllTextAsync(Path.Combine(run, "live-result.json"), Json.Write(segments));
    });
    await Test("Live worker cancellation exits while waiting for more audio", async () =>
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<LiveAudioClip>(); using var stop = new CancellationTokenSource();
        var task = new WorkerClient(paths).RunLiveAsync(new(Guid.NewGuid().ToString("N"), session.Id, "transcribe-live", "", run, options), channel.Reader, _ => Task.CompletedTask, stop.Token);
        await Task.Delay(300); stop.Cancel();
        try { await task.WaitAsync(TimeSpan.FromSeconds(10)); throw new Exception("实时工作进程未取消"); } catch (OperationCanceledException) { }
    });
}

if (args.Contains("--integration"))
{
    var paths = new AppPaths(Path.Combine(run, "integration-data"), Path.Combine(root, "engines"));
    var media = new MediaTools(paths); var assets = Path.Combine(root, "artifacts", "test-assets");
    var source = Path.Combine(assets, "jfk.wav");
    await Test("Native engine PE architecture verification", () => Sync(() =>
    {
        foreach (var id in new[] { "whisper-cuda", "whisper-cpu", "ffmpeg", "yt-dlp" })
        {
            var exe = paths.FindEngine(id, id.StartsWith("whisper") ? "whisper-cli.exe" : id == "ffmpeg" ? "ffmpeg.exe" : "yt-dlp.exe");
            PeArchitecture.RequireCompatible(exe);
        }
    }));
    await Test("FFmpeg audio extraction, Unicode path and waveform cache", async () =>
    {
        var directory = Path.Combine(run, "中文目录 空格"); Directory.CreateDirectory(directory);
        var wav = Path.Combine(directory, "提取 音频.wav"); await media.ExtractAudioAsync(source, wav, 0, 5, CancellationToken.None);
        Check(Math.Abs(await media.ProbeAsync(wav, CancellationToken.None) - 5) < 0.05, "提取片段时长错误");
        var waveform = await media.WaveformAsync(source, directory, CancellationToken.None); Check(waveform.Length > 0 && waveform.Max() == 1, "波形数据异常");
        var cached = await media.WaveformAsync(source, directory, CancellationToken.None); Check(cached.SequenceEqual(waveform), "波形缓存不同");
    });
    var benchmark = new List<object>();
    foreach (var backend in new[] { ComputeBackend.Cuda, ComputeBackend.Cpu })
    {
        await Test($"Worker process real {backend} transcription and resumable checkpoints", async () =>
        {
            var work = Path.Combine(run, backend.ToString()); Directory.CreateDirectory(work);
            var options = new AppSettings { EngineRoot = paths.EngineRoot, Backend = backend, TranscriptionModelPath = Path.Combine(assets, "ggml-tiny.en.bin"), Language = "en", EnableVad = false };
            var request = new JobRequest(Guid.NewGuid().ToString("N"), session.Id, "transcribe", source, work, options);
            var events = new List<JobEvent>(); var watch = Stopwatch.StartNew();
            await new WorkerClient(paths).RunAsync(request, e => { events.Add(e); return Task.CompletedTask; }, CancellationToken.None);
            var completed = events.Single(e => e.Type == "completed");
            var transcript = string.Join(" ", completed.Segments!.Select(s => s.Text));
            Check(transcript.Contains("country", StringComparison.OrdinalIgnoreCase) && transcript.Contains("Americans", StringComparison.OrdinalIgnoreCase), "转写没有识别测试音频");
            Check(completed.Backend == backend.ToString(), "实际后端错误");
            benchmark.Add(new { backend = completed.Backend, completed.Device, completed.ElapsedSeconds, completed.RealTimeFactor, wallSeconds = watch.Elapsed.TotalSeconds, transcript });
            await File.WriteAllTextAsync(Path.Combine(work, "events.json"), Json.Write(events));
            var cache = Directory.GetFiles(work, "*.segments.json", SearchOption.AllDirectories); Check(cache.Length == 1, "没有生成恢复检查点");
            var timestamp = File.GetLastWriteTimeUtc(cache[0]);
            await new WorkerClient(paths).RunAsync(request, _ => Task.CompletedTask, CancellationToken.None);
            Check(File.GetLastWriteTimeUtc(cache[0]) == timestamp, "恢复时重新执行了已完成片段");
        });
    }
    await File.WriteAllTextAsync(Path.Combine(run, "benchmark.json"), Json.Write(benchmark));
    await Test("Video subtitle burn-in export and reopen", async () =>
    {
        var input = Path.Combine(run, "input.mp4");
        Processes.EnsureSuccess(await Processes.RunAsync(media.Ffmpeg, ["-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", "color=c=0x162032:s=640x360:r=15:d=3", "-i", source, "-t", "3", "-c:v", "libx264", "-c:a", "aac", input]), "创建测试视频");
        var videoSession = new MediaSession { Title = "双语导出", MediaPath = input, Segments = [new() { Start = 0, End = 2.9, Text = "Native subtitle export", Translation = "原生双语字幕导出" }] };
        var output = Path.Combine(run, "双语成片.mp4");
        await media.ExportVideoAsync(videoSession, output, null, true, CancellationToken.None);
        Check(await media.ProbeAsync(output, CancellationToken.None) > 2.9, "导出视频无法重新读取");
    });
}
if (args.Contains("--ollama"))
{
    await Test("Existing Ollama real offline translation", async () =>
    {
        using var provider = new LocalLanguageModelProvider(new AppPaths(Path.Combine(run, "ollama")), new());
        var models = await provider.ListModelsAsync(CancellationToken.None); var model = models.FirstOrDefault(m => m.Contains("Index-Translate-2B")) ?? models.First(m => m.Contains("Index-Translate"));
        var answer = await provider.GenerateAsync(model, "将英文翻译成中文，只输出译文。", "Local transcription protects your privacy.", null, CancellationToken.None);
        Check(answer.Any(c => c >= 0x4e00 && c <= 0x9fff), "真实本地模型没有返回中文译文");
        await File.WriteAllTextAsync(Path.Combine(run, "ollama.txt"), answer);
    });
}
if (args.Contains("--speech"))
{
    var paths = new AppPaths(Path.Combine(run, "speech-data"), Path.Combine(root, "engines"));
    var speechRoot = Path.Combine(root, "artifacts", "test-assets", "speech");
    var kokoro = Path.Combine(speechRoot, "kokoro-multi-lang-v1_1");
    var output = Path.Combine(run, "中英配音.wav");
    var options = new AppSettings
    {
        EngineRoot = paths.EngineRoot, TtsModelPath = Path.Combine(kokoro, "model.onnx"), TtsVoicesPath = Path.Combine(kokoro, "voices.bin"),
        TtsTokensPath = Path.Combine(kokoro, "tokens.txt"), TtsDataDirectory = Path.Combine(kokoro, "espeak-ng-data"),
        TtsLexiconPath = Path.Combine(kokoro, "lexicon-us-en.txt") + "," + Path.Combine(kokoro, "lexicon-zh.txt"), TtsVoice = 50,
        SpeakerSegmentationModel = Path.Combine(speechRoot, "sherpa-onnx-pyannote-segmentation-3-0", "model.onnx"),
        SpeakerEmbeddingModel = Path.Combine(speechRoot, "speaker-embedding.onnx")
    };
    var cues = new[] { new SubtitleSegment { Start = 0, End = 6, Text = "This is a local transcription tool.", Translation = "这是一款本地音视频转写工具。" }, new SubtitleSegment { Start = 8, End = 15, Text = "Windows ARM64 and x64 are supported. Your content stays on this computer." } };
    await Test("Sherpa native worker generates Chinese/English time-aligned speech", async () =>
    {
        var request = new JobRequest(Guid.NewGuid().ToString("N"), session.Id, "tts", "", run, options, OutputPath: output, InputSegments: cues);
        await new WorkerClient(paths).RunAsync(request, _ => Task.CompletedTask, CancellationToken.None);
        var duration = await new MediaTools(paths).ProbeAsync(output, CancellationToken.None);
        Check(duration >= 8 && duration <= 15.1, "配音时间轴没有正确对齐");
        var clip = Directory.GetFiles(Path.Combine(run, "dub"), "*.wav").Length; Check(clip >= 2, "未保存逐句配音检查点");
    });
    await Test("Sherpa speaker segmentation and embeddings run offline", async () =>
    {
        var request = new JobRequest(Guid.NewGuid().ToString("N"), session.Id, "diarize", output, run, options, InputSegments: cues);
        JobEvent? final = null;
        await new WorkerClient(paths).RunAsync(request, e => { if (e.Type == "completed") final = e; return Task.CompletedTask; }, CancellationToken.None);
        Check(final?.Segments?.Any(s => s.Speaker.Length > 0) == true, "没有生成说话人标记");
    });
}
var gguf = args.FirstOrDefault(a => a.StartsWith("--gguf="))?[7..];
if (gguf is not null)
{
    await Test("Built-in llama.cpp CUDA loads GGUF and generates offline", async () =>
    {
        var options = new AppSettings { ModelProvider = LanguageModelKind.BuiltIn, GgufPath = gguf, Backend = ComputeBackend.Cuda };
        using var provider = new LocalLanguageModelProvider(new AppPaths(Path.Combine(run, "built-in"), Path.Combine(root, "engines")), options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var answer = await provider.GenerateAsync("local", "Translate English to Chinese. Output only the translation. /no_think", "Keep all media on this computer.", null, timeout.Token);
        Check(answer.Any(c => c >= 0x4e00 && c <= 0x9fff), "内置语言模型没有生成中文内容");
        await File.WriteAllTextAsync(Path.Combine(run, "built-in.txt"), answer);
    });
}
if (args.Contains("--online"))
{
    await Test("Bundled YouTube download to per-media CUDA transcription pipeline", async () =>
    {
        var paths = new AppPaths(Path.Combine(run, "online-data"), Path.Combine(root, "engines"));
        var sample = new MediaSession { Title = "Online pipeline fixture", Transcription = new() { ModelPath = Path.Combine(root, "artifacts", "test-assets", "ggml-tiny.en.bin"), Language = "en", Backend = ComputeBackend.Cuda, EnableVad = false, Threads = 2, BeamSize = 1, ChunkSeconds = 60, MaxLength = 28, Temperature = 0.1, Prompt = "zoo, elephants" } };
        sample.MediaPath = await new MediaTools(paths).DownloadAsync("https://www.youtube.com/watch?v=jNQXAC9IVRw", paths.SessionDirectory(sample.Id), CancellationToken.None, null, new(Quality: 3));
        sample.Duration = await new MediaTools(paths).ProbeAsync(sample.MediaPath, CancellationToken.None);
        var db = new Store(paths.DataRoot); db.SaveSession(sample);
        var finished = new TaskCompletionSource<JobRecord>(TaskCreationOptions.RunContinuationsAsynchronously); var received = new List<SubtitleSegment>();
        await using var scheduler = new JobScheduler(paths, db, () => new() { TranscriptionModelPath = "missing-global-model.bin" }, (j, e) =>
        {
            if (e?.Segments is { } cues) received = cues.ToList();
            if (j.State is JobState.Completed or JobState.Failed) finished.TrySetResult(j);
            return Task.CompletedTask;
        });
        var job = await scheduler.EnqueueAsync(sample, "transcribe");
        sample.Transcription.ModelPath = "next-task-only.bin";
        var result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(120));
        Check(result.State == JobState.Completed, result.Message);
        Check(job.Request!.Settings.TranscriptionModelPath.EndsWith("ggml-tiny.en.bin") && received.Count > 1 && received.Any(s => s.Text.Contains("elephant", StringComparison.OrdinalIgnoreCase)), "在线导入或任务独立参数没有传递给真实引擎");
        await File.WriteAllTextAsync(Path.Combine(run, "online-result.json"), Json.Write(new { backend = result.Backend, result.ElapsedSeconds, sample.Duration, segments = received }));
    });
}
if (args.Contains("--benchmark"))
{
    var paths = new AppPaths(Path.Combine(run, "benchmark-data"), Path.Combine(root, "engines"));
    var model = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoAI.Native", "models", "ggml-large-v3-turbo.bin");
    var audio = Path.Combine(root, "artifacts", "test-assets", "jfk.wav");
    var benchmark = new List<object>();
    foreach (var backend in new[] { ComputeBackend.Cuda, ComputeBackend.Vulkan, ComputeBackend.Cpu })
    {
        if (backend == ComputeBackend.Vulkan && !Directory.Exists(Path.Combine(paths.EngineRoot, AppPaths.Rid, "whisper-vulkan"))) { Console.WriteLine("SKIP Vulkan: engine still building"); continue; }
        await Test($"large-v3-turbo real {backend} benchmark", async () =>
        {
            var directory = Path.Combine(run, "large-" + backend); Directory.CreateDirectory(directory);
            var options = new AppSettings { EngineRoot = paths.EngineRoot, TranscriptionModelPath = model, Backend = backend, Language = "en", EnableVad = false };
            var request = new JobRequest(Guid.NewGuid().ToString("N"), session.Id, "transcribe", audio, directory, options);
            JobEvent? complete = null; var watch = Stopwatch.StartNew();
            await new WorkerClient(paths).RunAsync(request, e => { if (e.Type == "completed") complete = e; return Task.CompletedTask; }, CancellationToken.None);
            var text = string.Join(" ", complete!.Segments!.Select(s => s.Text)); Check(text.Contains("country", StringComparison.OrdinalIgnoreCase), "模型没有识别出测试音频");
            var logs = Directory.GetFiles(directory, "*.engine.log", SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();
            benchmark.Add(new { model = "large-v3-turbo", backend, complete.Device, complete.ElapsedSeconds, complete.RealTimeFactor, wallSeconds = watch.Elapsed.TotalSeconds, transcript = text, engineLog = logs.Single() });
        });
    }
    await File.WriteAllTextAsync(Path.Combine(run, "large-benchmark.json"), Json.Write(benchmark));
}
await File.WriteAllTextAsync(Path.Combine(run, "results.json"), Json.Write(results));
Console.WriteLine($"\n{results.Count - failures}/{results.Count} passed. Report: {run}");
Environment.ExitCode = failures == 0 ? 0 : 1;

sealed class FakeModel : ILocalLanguageModelProvider
{
    public int Calls;
    public string LastPrompt = "", LastSystem = "";
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["test"]);
    public Task<string> GenerateAsync(string model, string system, string prompt, Func<string, Task>? onDelta, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Calls++; LastPrompt = prompt; LastSystem = system; return Task.FromResult("译文"); }
}

sealed class DownloadHandler(byte[] data) : HttpMessageHandler
{
    public long? RangeStart;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RangeStart = request.Headers.Range?.Ranges.First().From;
        var start = (int)(RangeStart ?? 0);
        var response = new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(data[start..]) };
        if (start > 0) response.Content.Headers.ContentRange = new(start, data.Length - 1, data.Length);
        return Task.FromResult(response);
    }
}
