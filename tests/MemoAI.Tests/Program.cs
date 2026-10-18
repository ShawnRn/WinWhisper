using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using MemoAI.Core;

var repository = new DirectoryInfo(AppContext.BaseDirectory);
while (repository is not null && !(File.Exists(Path.Combine(repository.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(repository.FullName, "src", "MemoAI.Core")))) repository = repository.Parent;
var root = Path.GetFullPath(args.FirstOrDefault(a => a.StartsWith("--root="))?[7..] ?? repository?.FullName
    ?? throw new ArgumentException("无法定位测试仓库，请显式传入 --root=<repository>。"));
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
await Test("Deleting a recording removes owned audio, caches and jobs while preserving external and shared files", () => Sync(() =>
{
    var paths = new AppPaths(Path.Combine(run, "delete-recording"));
    var recordings = Path.Combine(paths.DataRoot, "recordings"); Directory.CreateDirectory(recordings);
    var audio = Path.Combine(recordings, "2026-10-18_13-24-59-abcdef.wav");
    var raw = Path.Combine(recordings, "2026-10-18_13-24-59-abcdef-system.wav");
    var unrelated = Path.Combine(recordings, "2026-10-18_13-24-59-fedcba.wav");
    var external = Path.Combine(run, "external-original.wav");
    foreach (var file in new[] { audio, raw, unrelated, external }) File.WriteAllText(file, "fixture");
    var recording = new MediaSession { MediaPath = external, RecordingPath = audio };
    var cache = paths.SessionDirectory(recording.Id); File.WriteAllText(Path.Combine(cache, "checkpoint.json"), "{}");
    var db = new Store(paths.DataRoot); db.SaveSession(recording); db.SaveJob(new() { SessionId = recording.Id, State = JobState.Completed });
    Check(db.GetSessionJobs(recording.Id).Count == 1, "会话任务查询错误");
    SessionFiles.DeleteOwned(paths, recording, []); db.DeleteSession(recording.Id);
    Check(!File.Exists(audio) && !File.Exists(raw) && !Directory.Exists(cache) && db.GetSession(recording.Id) is null && db.GetSessionJobs(recording.Id).Count == 0, "录音或会话缓存未清理");
    Check(File.Exists(external) && File.Exists(unrelated), "误删外部或其他录音");
    File.WriteAllText(audio, "fixture"); File.WriteAllText(raw, "fixture");
    SessionFiles.DeleteOwned(paths, new() { MediaPath = audio }, [new() { MediaPath = audio }]);
    Check(File.Exists(audio) && File.Exists(raw), "误删被另一个条目引用的录音");
    SessionFiles.DeleteOwned(paths, new() { RecordingPath = audio }, [new() { MediaPath = raw }]);
    Check(!File.Exists(audio) && File.Exists(raw), "误删仍被引用的原始采集音轨");
    File.WriteAllText(audio, "fixture");
    var invalid = new MediaSession { Id = "../outside", RecordingPath = audio };
    var rejected = false; try { SessionFiles.DeleteOwned(paths, invalid, []); } catch (ArgumentException) { rejected = true; }
    Check(rejected && File.Exists(audio), "未拒绝越界会话 ID");
    var owner = new MediaSession(); var ownerCache = paths.SessionDirectory(owner.Id);
    var sharedFile = Path.Combine(ownerCache, "download.mp4"); File.WriteAllText(sharedFile, "fixture");
    rejected = false; try { SessionFiles.DeleteOwned(paths, owner, [new() { MediaPath = sharedFile }]); } catch (IOException) { rejected = true; }
    Check(rejected && File.Exists(sharedFile), "误删其他条目引用的缓存媒体");
    var linked = new MediaSession(); var linkedCache = paths.SessionDirectory(linked.Id);
    // Junction creation works without the elevated symbolic-link privilege.
    var linkPath = Path.Combine(linkedCache, "external");
    var linkCommand = "New-Item -ItemType Junction -Path '" + linkPath.Replace("'", "''") + "' -Target '" + Path.GetDirectoryName(external)!.Replace("'", "''") + "' | Out-Null";
    var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(linkCommand)) }) start.ArgumentList.Add(argument);
    using (var junction = Process.Start(start)!) { junction.WaitForExit(); Check(junction.ExitCode == 0, "无法创建隔离测试 Junction"); }
    rejected = false; try { SessionFiles.DeleteOwned(paths, linked, []); } catch (IOException) { rejected = true; }
    Check(rejected && File.Exists(external) && Directory.Exists(linkedCache), "未拒绝缓存中的链接");
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
await Test("Smart English punctuation is idempotent and leaves machine content intact", () => Sync(() =>
{
    var input = "SpaceX's rocket, it's ready. The engineers' plan: \"Go now\", 'she said'. 'Twas the '90s. 中文'引号'。";
    var actual = ChineseTypography.Apply(input);
    Check(actual == "SpaceX’s rocket, it’s ready. The engineers’ plan: “Go now”, ‘she said’. ’Twas the ’90s. 中文「引号」。", actual);
    Check(ChineseTypography.Apply(actual) == actual, "弯引号不幂等");
    Check(ChineseTypography.Apply("'James' said 'yes'.") == "‘James’ said ‘yes’.", "以 s 结尾的引文被当成所有格");
    Check(ChineseTypography.Apply("I’ m Franz. it ’ s ready; SpaceX ' s rocket. We ’ re here, don ’ t stop.") == "I’m Franz. it’s ready; SpaceX’s rocket. We’re here, don’t stop.", "ASR 撇号周围空格未合并");
    Check(ChineseTypography.Apply("He said 's' and 'm'. Rock 'n' roll. I\n’ m") == "He said ‘s’ and ‘m’. Rock ‘n’ roll. I\n’ m", "错误合并引文或跨行缩写");
    foreach (var machine in new[] { "`it's 'raw'`", "```json\n{\"x\":\"it's\"}\n```", "https://example.com/it's", @"C:\Users\it's\file.txt", "{\"text\":\"it's\"}" })
        Check(ChineseTypography.Apply(machine) == machine, "机器内容被改写：" + machine);
}));
await Test("Subtitle export distinguishes original, translation and bilingual without shifting time", () => Sync(() =>
{
    foreach (var format in new[] { "srt", "vtt", "txt", "ass" })
    {
        var original = Subtitles.Export(session, format, SubtitleTextMode.Original);
        var translated = Subtitles.Export(session, format, SubtitleTextMode.Translation);
        var both = Subtitles.Export(session, format, SubtitleTextMode.Bilingual);
        Check(original.Contains("Hello, world.") && !original.Contains("你好"), "原文混入译文");
        Check(translated.Contains("你好") && !translated.Contains("Hello"), "译文混入原文");
        Check(both.Contains("Hello") && both.Contains("你好"), "双语丢失内容");
        if (format is "srt" or "vtt") Check(Subtitles.Parse(translated)[1].End == 11.999, "译文时间发生变化");
    }
    try { Subtitles.Export(new() { Segments = [new() { Text = "untranslated" }] }, "txt", SubtitleTextMode.Translation); throw new Exception("未翻译内容静默丢失"); } catch (InvalidOperationException) { }
}));
await Test("Sentence boundaries use original word times and reject fabricated alignment or paraphrase", () => Sync(() =>
{
    var cue = new SubtitleSegment { Start = 0, End = 5, Text = "hello everyone welcome to Texas", Words = [new("hello", .2, .5), new(" everyone", .5, 1.5), new(" welcome", 2.4, 3), new(" to", 3, 3.4), new(" Texas", 3.4, 5)] };
    var restored = SentenceSegmentation.RestorePunctuation(cue, "Hello everyone. Welcome to Texas!");
    Check(SentenceSegmentation.HasReliableWords(restored), "恢复标点丢失原词时间戳");
    var output = SentenceSegmentation.Optimize([restored], [new(1.5, 2.4)]);
    Check(output.Count == 2 && output[0].Start == .2 && output[0].End == 1.5 && output[1].Start == 2.4 && output[1].End == 5, "没有在实际词边界切分");
    Check(string.Join(" ", output.Select(s => s.Text)) == "Hello everyone. Welcome to Texas!", "断句丢词");
    try { SentenceSegmentation.RestorePunctuation(cue, "Hello everyone. Welcome to California!"); throw new Exception("改写单词被接受"); } catch (InvalidDataException) { }
    var coarse = new SubtitleSegment { Start = 10, End = 20, Text = "no word times do not guess" };
    var unchanged = SentenceSegmentation.Optimize([coarse]); Check(unchanged.Count == 1 && unchanged[0].Start == 10 && unchanged[0].End == 20, "伪造了词对齐");
    cue.Translation = "已有译文"; Check(SentenceSegmentation.Optimize([cue]).Single().Translation == "已有译文", "已有译文错位");
    cue.Translation = ""; cue.Words[2] = cue.Words[2] with { Start = .1 }; Check(!SentenceSegmentation.HasReliableWords(cue), "接受了倒序时间戳");
    using var tokens = JsonDocument.Parse("{\"tokens\":[{\"id\":50363,\"text\":\"[_BEG_]\",\"offsets\":{\"from\":0,\"to\":0}},{\"id\":1,\"text\":\" Space\",\"offsets\":{\"from\":100,\"to\":200}},{\"id\":2,\"text\":\"X's\",\"offsets\":{\"from\":200,\"to\":500}}]}");
    var words = SentenceSegmentation.ReadWhisperWords(tokens.RootElement, 30, 40); Check(words.Single() == new TimedWord(" SpaceX’s", 30.1, 30.5), "BPE 词片合并或绝对时间错误");
    var splitSuffix = SentenceSegmentation.ReadOnnxWords(["▁I", "’", "▁m", "▁here", "▁it", "’", "▁s", "▁ready"], [0, .2f, .3f, .5f, 1, 1.2f, 1.3f, 1.5f], [.2f, .1f, .2f, .5f, .2f, .1f, .2f, .5f], 30, 40);
    Check(splitSuffix.Count == 4 && splitSuffix[0].Text == " I’m" && splitSuffix[2].Text == " it’s" && Math.Abs(splitSuffix[0].End - 30.5) < .001, "缩写词片未按真实时间合并");
    var suffixCue = new SubtitleSegment { Start = 30, End = 32, Text = "I’ m here it’ s ready", Words = splitSuffix };
    Check(SentenceSegmentation.HasReliableWords(suffixCue) && SentenceSegmentation.Optimize([suffixCue]).Single().Text == "I’m here it’s ready", "断句重建文本重新引入空格或丢失对齐");
}));
await Test("English filler cleanup preserves content, real word times and literal examples", () => Sync(() =>
{
    var cue = new SubtitleSegment { Start = 0, End = 4, Text = "Um, hello uh world.", Translation = "旧译文", Words = [new("Um,", 0, .3), new(" hello", .5, 1), new(" uh", 1.2, 1.5), new(" world.", 2, 3)] };
    var clean = TranscriptEditing.RemoveFillers([cue]).Single();
    Check(clean.Text == "hello world." && clean.Translation == "" && clean.Start == 0 && clean.End == 4 && clean.Words.Select(w => (w.Start, w.End)).SequenceEqual(new[] { (.5, 1d), (2d, 3d) }) && SentenceSegmentation.HasReliableWords(clean), "清理引入虚假时间或丢词");
    Check(cue.Text.StartsWith("Um") && cue.Translation.Length > 0, "预览前修改了原对象");
    Check(TranscriptEditing.CleanFillers("umami umbrella Uhuru like well right") == "umami umbrella Uhuru like well right", "删除了有意义的词");
    Check(TranscriptEditing.CleanFillers("Say ‘um’ here.") == "Say ‘um’ here." && TranscriptEditing.CleanFillers("`um` uh") == "`um` uh", "误改字面示例或代码");
    Check(TranscriptEditing.RemoveFillers([new() { Start = 0, End = 1, Text = "uh, um." }]).Count == 0, "未清理纯语气词字幕");
}));
await Test("AI transcript patches validate all IDs and preserve timing without inventing alignment", () => Sync(() =>
{
    var cue = new SubtitleSegment { Id = "a", Start = 10, End = 12, Text = "hello texas", Translation = "旧译文", Words = [new("hello", 10, 11), new(" texas", 11, 12)] };
    var punctuation = TranscriptEditing.ParseEdits([cue], "{\"edits\":[{\"id\":\"a\",\"text\":\"Hello Texas!\"}]}", true).Single();
    Check(punctuation.Start == 10 && punctuation.End == 12 && SentenceSegmentation.HasReliableWords(punctuation), "标点恢复丢失实际时间");
    var spelling = TranscriptEditing.ParseEdits([cue], "{\"edits\":[{\"id\":\"a\",\"text\":\"Hello California!\",\"start\":999}]}", false).Single();
    Check(spelling.Start == 10 && spelling.End == 12 && spelling.Words.Count == 0 && spelling.Translation == "", "纠错伪造了词时间或保留失效译文");
    foreach (var invalid in new[] { "{\"edits\":[]}", "{\"edits\":[{\"id\":\"wrong\",\"text\":\"hello\"}]}", "{\"edits\":[{\"id\":\"a\",\"text\":\"\"}]}" })
    { var rejected = false; try { TranscriptEditing.ParseEdits([cue], invalid, false); } catch (InvalidDataException) { rejected = true; } Check(rejected, "接受了不完整 / 错误补丁"); }
    var changedWords = false; try { TranscriptEditing.ParseEdits([cue], "{\"edits\":[{\"id\":\"a\",\"text\":\"hello California\"}]}", true); } catch (InvalidDataException) { changedWords = true; }
    Check(changedWords && cue.Text == "hello texas", "标点模式允许改写词语或提前写入");
}));
await Test("AI transcript editing covers every batch and cancellation keeps original subtitles", async () =>
{
    var source = Enumerable.Range(0, 70).Select(i => new SubtitleSegment { Text = $"hello item {i}", Start = i * 2, End = i * 2 + 1 }).ToArray();
    var model = new TranscriptFixtureModel(); var output = await TranscriptEditing.ProposeAsync(model, new(), source, "punctuate", true, null, CancellationToken.None);
    Check(output.Count == 70 && model.Seen.SequenceEqual(source.Select(s => s.Id)) && output.All(s => s.Text.EndsWith('.')), "未覆盖全文或遗漏补丁");
    Check(source.All(s => !s.Text.EndsWith('.')), "预览生成提前改写原文");
    using var stop = new CancellationTokenSource(); model = new TranscriptFixtureModel { Stop = stop };
    var cancelled = false; try { await TranscriptEditing.ProposeAsync(model, new(), source, "punctuate", true, null, stop.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled && source.All(s => !s.Text.EndsWith('.')), "取消没有保留原文");
});
await Test("Transcript word diffs preserve context and highlight only changed words", () => Sync(() =>
{
    var diff = TranscriptTextDiff.Compare("we are here at spacex uh rocket test site", "we are here at SpaceX rocket test site");
    Check(string.Concat(diff.Before.Select(p => p.Text)) == "we are here at spacex uh rocket test site" && string.Concat(diff.After.Select(p => p.Text)) == "we are here at SpaceX rocket test site", "Diff 丢失原始文字");
    Check(string.Concat(diff.After.Where(p => p.Changed).Select(p => p.Text)) == "SpaceX" && diff.Before.Where(p => p.Changed).All(p => !p.Text.Contains("rocket")), "整句被标为修改");
    foreach (var pair in new[] { ("你好世界 👩🏽‍💻", "你好朋友 👩🏽‍💻"), ("It’s local.", "It’s local!"), ("one one two", "one two"), ("", "新增"), ("删除", ""), ("same", "same") })
    {
        var compared = TranscriptTextDiff.Compare(pair.Item1, pair.Item2);
        Check(string.Concat(compared.Before.Select(p => p.Text)) == pair.Item1 && string.Concat(compared.After.Select(p => p.Text)) == pair.Item2, "字符、重复词或空内容 Diff 不可重建");
        Check(!compared.Before.Concat(compared.After).Any(p => p.Changed && p.Text.Contains("👩")), "未改变的 emoji 被拆分");
    }
    var longDiff = TranscriptTextDiff.Compare("X " + new string('中', 2500) + " end", "Y " + new string('文', 2500) + " end");
    Check(string.Concat(longDiff.After.Select(p => p.Text)).EndsWith(" end") && longDiff.After[^1] is { Changed: false, Text: " end" }, "大输入没有保留公共后缀");
}));
await Test("Transcript proposals persist, allow/reject is atomic and stale edits cannot overwrite", () => Sync(() =>
{
    var dataRoot = Path.Combine(run, "revision-history"); var db = new Store(dataRoot);
    var media = new MediaSession { Segments = [new() { Start = 0, End = 2, Text = "before", Translation = "原译文" }] }; db.SaveSession(media);
    var before = media.Segments.Select(c => c.Copy()).ToArray(); var after = before.Select(c => { var copy = c.Copy(); copy.Text = "after"; copy.Translation = ""; return copy; }).ToArray();
    var proposal = db.CreateTranscriptProposal(media, before, after, "fixture edit");
    var stale = db.CreateTranscriptProposal(media, before, after, "parallel proposal");
    var aba = db.CreateTranscriptProposal(media, before, after, "ABA guard");
    db = new Store(dataRoot); Check(db.GetTranscriptVersion(media.Id, proposal.Id).Status == "Pending" && db.GetSession(media.Id)!.Segments[0].Text == "before", "重启丢失提案或预览提前写入");
    var rejected = db.CreateTranscriptProposal(media, before, after, "reject fixture"); db.DecideTranscriptProposal(media, rejected.Id, false); db.DecideTranscriptProposal(media, rejected.Id, false);
    Check(db.GetSession(media.Id)!.Segments[0].Text == "before" && db.GetTranscriptVersion(media.Id, rejected.Id).Status == "Rejected", "拒绝修改了原文");
    db.DecideTranscriptProposal(media, proposal.Id, true); var accepted = db.GetSession(media.Id)!;
    Check(accepted.Segments[0].Text == "after" && db.GetTranscriptVersion(media.Id, proposal.Id).Status == "Applied", "版本与字幕没有一同提交");
    var conflict = false; try { db.DecideTranscriptProposal(accepted, stale.Id, true); } catch (InvalidOperationException) { conflict = true; }
    Check(conflict && db.GetSession(media.Id)!.Segments[0].Text == "after", "旧提案覆盖了新字幕"); db.DecideTranscriptProposal(accepted, stale.Id, false);
    var rollback = db.CreateTranscriptProposal(accepted, accepted.Segments.ToArray(), before, "rollback fixture", kind: "Rollback");
    db.DecideTranscriptProposal(accepted, rollback.Id, true);
    Check(db.GetSession(media.Id)!.Segments[0].Text == "before" && db.GetTranscriptVersions(media.Id).Any(v => v.Id == proposal.Id && v.Status == "Applied") && rollback.ParentId == proposal.Id, "回滚丢失历史或没有关联父版本");
    var abaRejected = false; try { db.DecideTranscriptProposal(db.GetSession(media.Id)!, aba.Id, true); } catch (InvalidOperationException) { abaRejected = true; }
    Check(abaRejected && db.GetTranscriptHead(media.Id) == rollback.Id, "内容相同但父版本不同的过期提案被应用");
    var rows = TranscriptDiff.Build(before, after); Check(rows.Count == 1 && rows[0].Before.Contains("before") && rows[0].After.Contains("after"), "缺失原文 / 译文 diff");
    db.DeleteSession(media.Id); Check(db.GetTranscriptVersions(media.Id).Count == 0, "移除媒体遗留了版本字幕数据");
}));
await Test("Transcript Agent tools are bounded, scoped and preserve full edit coverage", async () =>
{
    var source = Enumerable.Range(0, 23).Select(i => new SubtitleSegment { Text = $"item {i}", Start = i * 2, End = i * 2 + 1 }).ToArray();
    var model = new HarnessFixtureModel(); var harness = new TranscriptAgentHarness();
    var result = await harness.RunAsync(model, new(), source, "punctuate", null, CancellationToken.None);
    Check(result.Count == 23 && model.Editor.Seen.SequenceEqual(source.Select(c => c.Id)), "Agent 未处理全文");
    Check(harness.Events.Any(e => e.Tool == "search_transcript") && harness.Events.Any(e => e.State == "rejected") && harness.Events.Select(e => e.Sequence).SequenceEqual(Enumerable.Range(1, harness.Events.Count)) && harness.Events.All(e => e.RunId == harness.RunId), "能力边界或事件关联错误");
    var db = new Store(Path.Combine(run, "agent-audit")); var media = new MediaSession { Segments = new(source) }; db.SaveSession(media);
    var record = db.CreateTranscriptProposal(media, source, [], "agent fixture", status: "Generating", kind: "Agent"); record.Events = harness.Events;
    record.Status = "Cancelled"; db.UpdateTranscriptRun(record);
    Check(new Store(Path.Combine(run, "agent-audit")).GetTranscriptVersion(media.Id, record.Id).Events.Count == harness.Events.Count && db.GetSession(media.Id)!.Segments.Count == 23, "停止后丢失运行记录或修改字幕");
});
await Test("Translation API sends only explicit text and does not leak error bodies", async () =>
{
    var fake = new TranslationHandler(); using var api = new TranslationApi("https://example.invalid/v1", "fixture-secret", fake);
    var value = await api.GenerateAsync("test-model", "translate", "It’s local text.", null, CancellationToken.None);
    using var body = JsonDocument.Parse(fake.Body);
    Check(value == "译文" && fake.Path == "/v1/chat/completions" && body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == "It’s local text." && fake.Auth == "Bearer fixture-secret", "请求协议错误");
    Check(!fake.Body.Contains("MediaPath") && !fake.Body.Contains("fixture-secret"), "凭据或媒体路径出现在载荷");
    fake.Fail = true;
    try { await api.GenerateAsync("test", "s", "p", null, CancellationToken.None); throw new Exception("未拒绝错误"); } catch (HttpRequestException ex) { Check(!ex.Message.Contains("private-error"), "泄漏服务响应体"); }
    foreach (var address in new[] { "http://external.invalid/v1", "https://user:secret@example.invalid/v1", "https://example.invalid/v1?key=secret" })
        try { TranslationApi.ValidateEndpoint(address); throw new Exception("地址未拒绝"); } catch (ArgumentException) { }
    Check(TranslationApi.ValidateEndpoint("http://127.0.0.1:1234/v1").AbsolutePath == "/v1/chat/completions", "本地兼容 API 不可用");
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
await Test("Questions inspect all transcript chunks including late evidence", async () =>
{
    var model = new FakeModel(); var media = new MediaSession { Segments = new(Enumerable.Range(0, 300).Select(i => new SubtitleSegment { Start = i * 5, End = i * 5 + 4, Text = new string('a', 110) + (i == 299 ? " delivery in 2028 END_EVIDENCE" : " cue " + i) })) };
    await new AiService(model, new()).AskAsync(media, "交付时间是哪年？", null, CancellationToken.None);
    Check(model.Prompts.Any(p => p.Contains("END_EVIDENCE")), "最后一段转写没有进入任何模型请求");
    Check(Enumerable.Range(0, 299).All(i => model.Prompts.Any(p => p.Contains("cue " + i + "\n"))), "中间转写被截断");
    Check(model.LastPrompt.Contains("全部 300 条转写"), "最终回答没有完整核对范围");
});
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
        Check(StoreModels.All.Any(s => s.Asr == m) == (m.Id != "funasr-nano-fp16"), "商店应展示可用模型并收起未通过内容校验的 FP16 版本");
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
            options.Backend = ComputeBackend.Cpu;
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
            var unsupported = Json.Read<AppSettings>(Json.Write(options)); unsupported.Backend = ComputeBackend.Vulkan;
            try { SherpaTranscription.Configuration(unsupported.TranscriptionModelPath, unsupported); throw new Exception("错误报告 ONNX Vulkan 支持"); } catch (NotSupportedException) { }
        });
    }
}

await Test("GPU evidence rejects provider fallback and native inference errors", () => Sync(() =>
{
    var words = SentenceSegmentation.ReadOnnxWords([" coun", "tr", "y", "."], [1, 1.5f, 2, 2], [.5f, .5f, 0, .1f], 0, 3);
    Check(words.Count == 1 && words[0].Text == " country." && words[0].Start == 1 && Math.Abs(words[0].End - 2.1) < .001, "零时长 BPE 片段导致完整词时间丢失");
    Check(SentenceSegmentation.ReadOnnxWords([" word"], [1], [0], 0, 3).Count == 0, "为零时长单词伪造了时间范围");
    var folder = Path.Combine(run, "gpu-proof"); Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "profile-test.json"), "[{\"args\":{\"provider\":\"CPUExecutionProvider\"}}]");
    File.WriteAllText(Path.Combine(folder, "profile-incomplete.json"), "[{broken overlapping native profile");
    try { SherpaRuntimes.ReadProfile(folder, "cuda"); throw new Exception("CPU 被误报为 CUDA"); } catch (InvalidOperationException) { }
    File.WriteAllText(Path.Combine(folder, "profile-test.json"), "[{\"args\":{\"provider\":\"CUDAExecutionProvider\"}},{\"args\":{\"provider\":\"CPUExecutionProvider\"}}]");
    Check(SherpaRuntimes.ReadProfile(folder, "cuda") == (1, 1), "GPU / CPU 执行次数错误");
    Check(SherpaRuntimes.IsNativeFailure("Non-zero status code returned while running Conv node") && SherpaRuntimes.IsNativeFailure("cuDNN is unavailable") && !SherpaRuntimes.IsNativeFailure("Some nodes were not assigned to the preferred execution providers"), "原生失败检测错误");
}));
if (args.Contains("--transcript-edit"))
{
    await Test("Real local AI returns validated transcript punctuation patches with word times", async () =>
    {
        var options = new AppSettings(); var paths = new AppPaths(Path.Combine(run, "real-edit"), Path.Combine(root, "engines"));
        using var model = new LocalLanguageModelProvider(paths, options); using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var source = new[] { new SubtitleSegment { Id = "line-1", Start = 0, End = 2, Text = "hello everyone", Words = [new("hello", 0, 1), new(" everyone", 1, 2)] }, new SubtitleSegment { Id = "line-2", Start = 2, End = 4, Text = "welcome to texas", Words = [new("welcome", 2, 3), new(" to", 3, 3.5), new(" texas", 3.5, 4)] } };
        var edited = await TranscriptEditing.ProposeAsync(model, options, source, "Restore English punctuation and capitalization only.", true, null, stop.Token);
        Check(edited.Count == source.Length && edited.All(SentenceSegmentation.HasReliableWords) && source[0].Text == "hello everyone", "本地模型补丁丢失词时间或提前修改原文");
        await File.WriteAllTextAsync(Path.Combine(run, "real-transcript-edit.json"), Json.Write(edited));
        var harness = new TranscriptAgentHarness();
        var agentEdited = await harness.RunAsync(model, options, source, "Correct capitalization and punctuation only, preserving all words and numbers. Generate an editing preview.", null, stop.Token);
        Check(agentEdited.Count == source.Length && harness.Events.Any(e => e.Tool == "propose_edits" && e.State == "completed"), "真实本地 Agent 未生成有效提案");
        await File.WriteAllTextAsync(Path.Combine(run, "real-transcript-agent.json"), Json.Write(new { events = harness.Events, edited = agentEdited }));
    });
}
var gpuTest = args.FirstOrDefault(a => a.StartsWith("--asr-gpu="))?[10..];
if (gpuTest is not null)
{
    var paths = new AppPaths(Path.Combine(run, "gpu-data"), Path.Combine(root, "engines"));
    foreach (var choice in gpuTest.Split(','))
    {
        var parts = choice.Split(':'); var model = AsrModels.All.First(m => m.Id == parts[0]);
        var backend = Enum.Parse<ComputeBackend>(parts[1], true);
        await Test("Real ONNX GPU worker, executed kernels and cache " + choice, async () =>
        {
            var modelRoot = Path.Combine(root, "artifacts", "test-assets", "asr-models");
            if (!AsrModels.IsInstalled(model, modelRoot))
            {
                Console.WriteLine($"Downloading GPU test model {model.Name} ({model.Bytes / 1048576} MB)");
                await AsrModels.InstallAsync(model, modelRoot, null, CancellationToken.None);
            }
            var audio = Path.Combine(root, "artifacts", "test-assets", "jfk.wav");
            var language = "en";
            if (model.LanguageCodes.SequenceEqual(new[] { "zh" })) { audio = Directory.GetFiles(Path.GetDirectoryName(model.ManifestPath(modelRoot))!, "*.wav", SearchOption.AllDirectories).First(); language = "zh"; }
            var options = new AppSettings { Backend = backend, EngineRoot = paths.EngineRoot, TranscriptionModelPath = model.ManifestPath(modelRoot), Language = language };
            var request = new JobRequest(Guid.NewGuid().ToString("N"), session.Id, "transcribe", audio, Path.Combine(run, choice.Replace(':', '-')), options);
            JobEvent? final = null; using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            await new WorkerClient(paths).RunAsync(request, e => { if (e.Type == "completed") final = e; return Task.CompletedTask; }, stop.Token);
            Check(final?.Backend == backend.ToString() && final.Device?.Contains("GPU") == true, "未确认实际 GPU 执行");
            Check(final!.Segments?.Length > 0, "GPU 返回空字幕");
            if (language == "en") Check(string.Join(' ', final.Segments!.Select(s => s.Text)).Contains("country", StringComparison.OrdinalIgnoreCase), "GPU 识别结果错误");
            if (model.Id == "parakeet-v3") Check(final.Segments!.Any(SentenceSegmentation.HasReliableWords) && final.Segments.Length >= 2, "原生 token 时长丢失，英语断句没有使用真实时间戳");
            await File.WriteAllTextAsync(Path.Combine(run, choice.Replace(':', '-') + ".json"), Json.Write(final));
            await new WorkerClient(paths).RunAsync(request, e => { if (e.Type == "completed") final = e; return Task.CompletedTask; }, stop.Token);
            Check(final?.Backend == "Cache" && final.Segments?.Length > 0, "GPU 检查点未恢复或被误报成新 GPU 执行");
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
            Check(completed.Segments!.Any(SentenceSegmentation.HasReliableWords) && completed.Segments.All(s => s.End > s.Start && s.Start >= 0), "真实模型词时间戳未保留或断句时间无效");
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
var speechGpu = args.FirstOrDefault(a => a.StartsWith("--speech-gpu="))?[13..];
var diarizeGpu = args.FirstOrDefault(a => a.StartsWith("--diarize-gpu="))?[14..];
if (args.Contains("--speech") || speechGpu is not null || diarizeGpu is not null)
{
    var paths = new AppPaths(Path.Combine(run, "speech-data"), Path.Combine(root, "engines"));
    var speechRoot = Path.Combine(root, "artifacts", "test-assets", "speech");
    var kokoro = Path.Combine(speechRoot, "kokoro-multi-lang-v1_1");
    var output = Path.Combine(run, "中英配音.wav");
    var options = new AppSettings
    {
        Backend = speechGpu is null && diarizeGpu is null ? ComputeBackend.Cpu : Enum.Parse<ComputeBackend>(speechGpu ?? diarizeGpu!, true),
        EngineRoot = paths.EngineRoot, TtsModelPath = Path.Combine(kokoro, "model.onnx"), TtsVoicesPath = Path.Combine(kokoro, "voices.bin"),
        TtsTokensPath = Path.Combine(kokoro, "tokens.txt"), TtsDataDirectory = Path.Combine(kokoro, "espeak-ng-data"),
        TtsLexiconPath = Path.Combine(kokoro, "lexicon-us-en.txt") + "," + Path.Combine(kokoro, "lexicon-zh.txt"), TtsVoice = 50,
        SpeakerSegmentationModel = Path.Combine(speechRoot, "sherpa-onnx-pyannote-segmentation-3-0", "model.onnx"),
        SpeakerEmbeddingModel = Path.Combine(speechRoot, "speaker-embedding.onnx")
    };
    var cues = new[] { new SubtitleSegment { Start = 0, End = 6, Text = "This is a local transcription tool.", Translation = "这是一款本地音视频转写工具。" }, new SubtitleSegment { Start = 8, End = 15, Text = "Windows ARM64 and x64 are supported. Your content stays on this computer." } };
    if (diarizeGpu is null) await Test("Sherpa native worker generates Chinese/English time-aligned speech", async () =>
    {
        var request = new JobRequest(Guid.NewGuid().ToString("N"), session.Id, "tts", "", run, options, OutputPath: output, InputSegments: cues);
        JobEvent? final = null;
        await new WorkerClient(paths).RunAsync(request, e => { if (e.Type == "completed") final = e; return Task.CompletedTask; }, CancellationToken.None);
        if (speechGpu is not null) Check(final?.Backend == options.Backend.ToString() && final.Device?.Contains("GPU 已验证") == true, "配音没有验证 GPU 执行");
        var duration = await new MediaTools(paths).ProbeAsync(output, CancellationToken.None);
        Check(duration >= 8 && duration <= 15.1, "配音时间轴没有正确对齐");
        var clip = Directory.GetFiles(Path.Combine(run, "dub"), "*.wav").Length; Check(clip >= 2, "未保存逐句配音检查点");
    });
    await Test("Sherpa speaker segmentation and embeddings run offline", async () =>
    {
        var request = new JobRequest(Guid.NewGuid().ToString("N"), session.Id, "diarize", File.Exists(output) ? output : Path.Combine(root, "artifacts", "test-assets", "jfk.wav"), run, options, InputSegments: cues);
        JobEvent? final = null;
        await new WorkerClient(paths).RunAsync(request, e => { if (e.Type == "completed") final = e; return Task.CompletedTask; }, CancellationToken.None);
        Check(final?.Segments?.Any(s => s.Speaker.Length > 0) == true, "没有生成说话人标记");
        if (speechGpu is not null || diarizeGpu is not null) Check(final?.Backend == options.Backend.ToString() && final.Device?.Contains("GPU 已验证") == true, "说话人模型没有验证 GPU 执行");
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

sealed class HarnessFixtureModel : ILocalLanguageModelProvider
{
    private int decisions; public TranscriptFixtureModel Editor = new();
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Editor.ListModelsAsync(ct);
    public Task<string> GenerateAsync(string model, string system, string prompt, Func<string, Task>? delta, CancellationToken ct)
    {
        if (!system.Contains("Choose exactly ONE tool")) return Editor.GenerateAsync(model, system, prompt, delta, ct);
        return Task.FromResult(++decisions switch { 1 => "{\"tool\":\"read_transcript\",\"offset\":0}", 2 => "{\"tool\":\"search_transcript\",\"query\":\"item\"}", 3 => "{\"tool\":\"shell\"}", _ => "{\"tool\":\"propose_edits\"}" });
    }
}
sealed class TranscriptFixtureModel : ILocalLanguageModelProvider
{
    public List<string> Seen = []; public CancellationTokenSource? Stop;
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["fixture"]);
    public Task<string> GenerateAsync(string model, string system, string prompt, Func<string, Task>? delta, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(prompt); var rows = json.RootElement.GetProperty("transcript").EnumerateArray().ToArray();
        Seen.AddRange(rows.Select(r => r.GetProperty("id").GetString()!));
        if (Seen.Count > 18) Stop?.Cancel();
        return Task.FromResult(Json.Write(new { edits = rows.Select(r => new { id = r.GetProperty("id").GetString(), text = r.GetProperty("text").GetString() + "." }) }));
    }
}
sealed class FakeModel : ILocalLanguageModelProvider
{
    public int Calls;
    public string LastPrompt = "", LastSystem = "";
    public List<string> Prompts = [];
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["test"]);
    public Task<string> GenerateAsync(string model, string system, string prompt, Func<string, Task>? onDelta, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Calls++; Prompts.Add(prompt); LastPrompt = prompt; LastSystem = system; return Task.FromResult("译文"); }
}

sealed class TranslationHandler : HttpMessageHandler
{
    public string Path = "", Body = "", Auth = ""; public bool Fail;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Path = request.RequestUri!.AbsolutePath; Body = await request.Content!.ReadAsStringAsync(cancellationToken); Auth = request.Headers.Authorization!.ToString();
        return new(Fail ? HttpStatusCode.BadRequest : HttpStatusCode.OK) { Content = new StringContent(Fail ? "private-error fixture-secret" : "{\"choices\":[{\"message\":{\"content\":\"译文\"}}]}") };
    }
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
