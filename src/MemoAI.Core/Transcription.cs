using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace MemoAI.Core;

public sealed class WhisperEngine(AppPaths paths) : ITranscriptionEngine
{
    public async Task TranscribeAsync(JobRequest request, Func<JobEvent, Task> progress, CancellationToken ct)
    {
        if (!File.Exists(request.Settings.TranscriptionModelPath)) throw new FileNotFoundException("请先在「模型与设置」下载或选择 Whisper 模型。");
        _ = TranscriptionOptions.FromSettings(request.Settings).ApplyTo(request.Settings);
        if (request.Settings.EnableVad && !File.Exists(request.Settings.VadModelPath))
            await progress(new("warning", "尚未配置 Silero VAD 模型，本次转写不启用静音检测；可在模型商店一键安装。"));
        var media = new MediaTools(paths);
        var duration = await media.ProbeAsync(request.MediaPath, ct);
        var start = request.RangeStart ?? 0;
        var end = Math.Min(request.RangeEnd ?? duration, duration);
        if (end <= start) throw new InvalidDataException("媒体没有可处理的音频时长。");
        var candidates = request.Settings.Backend == ComputeBackend.Auto ? new[] { ComputeBackend.Cuda, ComputeBackend.Vulkan, ComputeBackend.Cpu } : [request.Settings.Backend];
        Exception? last = null;
        foreach (var backend in candidates)
        {
            try { await RunBackendAsync(backend, duration, start, end); return; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                last = ex;
                if (request.Settings.Backend != ComputeBackend.Auto) throw;
                await progress(new("warning", $"{backend} 未通过验证或运行失败：{ex.Message}。尝试下一后端。"));
            }
        }
        throw new InvalidOperationException("所有转写后端均失败。", last);

        async Task RunBackendAsync(ComputeBackend backend, double fullDuration, double begin, double finish)
        {
            var id = backend switch { ComputeBackend.Cuda => "whisper-cuda", ComputeBackend.Vulkan => "whisper-vulkan", _ => "whisper-cpu" };
            var cli = paths.FindEngine(id, "whisper-cli.exe");
            foreach (var dll in Directory.EnumerateFiles(Path.GetDirectoryName(cli)!, "*.dll")) PeArchitecture.RequireCompatible(dll);
            var key = MediaTools.CacheKey(request.MediaPath, request.Settings.TranscriptionModelPath,
                Json.Write(new { engine = "b5454-word-boundaries-v2", backend, options = TranscriptionOptions.FromSettings(request.Settings), request.Settings.AutoSegmentEnglish, request.Settings.VadModelPath, begin, finish }));
            var cache = Path.Combine(request.WorkDirectory, "transcription", key); Directory.CreateDirectory(cache);
            var all = new List<SubtitleSegment>(); var watch = Stopwatch.StartNew(); var device = backend == ComputeBackend.Cpu ? $"{AppPaths.Rid} CPU" : "";
            var chunkSeconds = request.Settings.TranscriptionChunkSeconds;
            var chunkCount = (int)Math.Ceiling((finish - begin) / chunkSeconds);
            for (var chunk = 0; chunk < chunkCount; chunk++)
            {
                ct.ThrowIfCancellationRequested();
                var offset = begin + chunk * chunkSeconds; var length = Math.Min(chunkSeconds, finish - offset);
                var output = Path.Combine(cache, $"chunk-{chunk:00000}");
                var checkpoint = output + ".segments.json";
                await progress(new("progress", $"{backend} · 片段 {chunk + 1}/{chunkCount}", (double)chunk / chunkCount, Backend: backend.ToString(), Device: device));
                List<SubtitleSegment> segments;
                if (File.Exists(checkpoint))
                {
                    segments = Json.Read<List<SubtitleSegment>>(await File.ReadAllTextAsync(checkpoint, ct));
                    foreach (var segment in segments) segment.Text = ChineseTypography.Apply(segment.Text);
                    if (File.Exists(output + ".engine.log"))
                    {
                        var cachedLog = await File.ReadAllTextAsync(output + ".engine.log", ct);
                        var cachedDevice = Regex.Match(cachedLog, @"(?im)(?:Device\s+\d+|device\s+\d+)\s*:\s*([^\r\n]+)");
                        if (cachedDevice.Success) device = cachedDevice.Groups[1].Value.Trim();
                    }
                    await progress(new("progress", $"从检查点恢复片段 {chunk + 1}/{chunkCount}", (double)chunk / chunkCount, Backend: backend.ToString(), Device: device));
                }
                else
                {
                    var wav = output + ".wav";
                    await progress(new("progress", $"解码音频 · 片段 {chunk + 1}/{chunkCount}", (double)chunk / chunkCount, Backend: backend.ToString(), Device: device));
                    await media.ExtractAudioAsync(request.MediaPath, wav, offset, length, ct);
                    var threads = request.Settings.TranscriptionThreads == 0 ? Math.Clamp(Environment.ProcessorCount / 2, 2, 8) : request.Settings.TranscriptionThreads;
                    var args = new List<string> { "-m", request.Settings.TranscriptionModelPath, "-f", wav, "-l", request.Settings.Language, "-ojf", "-of", output, "-pp", "-t", threads.ToString(CultureInfo.InvariantCulture), "-bs", request.Settings.TranscriptionBeamSize.ToString(CultureInfo.InvariantCulture), "-tp", request.Settings.TranscriptionTemperature.ToString(CultureInfo.InvariantCulture) };
                    if (request.Settings.TranscriptionMaxLength > 0) args.AddRange(["-ml", request.Settings.TranscriptionMaxLength.ToString(CultureInfo.InvariantCulture), "-sow"]);
                    if (backend == ComputeBackend.Cpu) args.Add("-ng");
                    if (request.Settings.EnableVad && File.Exists(request.Settings.VadModelPath)) args.AddRange(["--vad", "--vad-model", request.Settings.VadModelPath]);
                    // Whisper's decoder prompt is recognition context, not a chat system message.
                    // Layout policy is applied to the resulting text; only media-specific vocabulary belongs here.
                    if (!string.IsNullOrWhiteSpace(request.Settings.Glossary)) args.AddRange(["--prompt", request.Settings.Glossary]);
                    var updates = Channel.CreateUnbounded<JobEvent>(new UnboundedChannelOptions { SingleReader = true });
                    var relay = Task.Run(async () => { await foreach (var update in updates.Reader.ReadAllAsync(ct)) await progress(update); }, ct);
                    ProcessResult result;
                    var detectedDevice = "";
                    try
                    {
                        result = await Processes.RunAsync(cli, args, onError: line =>
                        {
                            var detected = Regex.Match(line, @"(?i)Device\s+\d+\s*:\s*(.+)|ggml_vulkan:\s*\d+\s*=\s*(.+)");
                            if (detected.Success) detectedDevice = (detected.Groups[1].Success ? detected.Groups[1].Value : detected.Groups[2].Value).Trim();
                            if (Regex.IsMatch(line, @"(?i)using (CUDA|Vulkan)\d* backend"))
                            {
                                device = detectedDevice.Length > 0 ? detectedDevice : backend.ToString();
                                updates.Writer.TryWrite(new("progress", $"加载模型 · 片段 {chunk + 1}/{chunkCount}", (double)chunk / chunkCount, Backend: backend.ToString(), Device: device));
                            }
                            var percent = Regex.Match(line, @"progress\s*=\s*(\d+)%");
                            if (percent.Success)
                            {
                                var local = int.Parse(percent.Groups[1].Value, CultureInfo.InvariantCulture) / 100d;
                                updates.Writer.TryWrite(new("progress", $"{backend} · 片段 {chunk + 1}/{chunkCount} · {local:P0}", (chunk + local) / chunkCount, Backend: backend.ToString(), Device: device));
                            }
                        }, ct: ct);
                    }
                    finally { updates.Writer.TryComplete(); await relay; }
                    await File.WriteAllTextAsync(output + ".engine.log", result.Error + result.Output, ct);
                    Processes.EnsureSuccess(result, $"{backend} 转写");
                    if (backend == ComputeBackend.Cuda && !Regex.IsMatch(result.Error, @"(?i)using CUDA\d* backend"))
                        throw new InvalidOperationException("CUDA 引擎没有报告 GPU 初始化成功，拒绝将 CPU 执行标记为 CUDA。");
                    if (backend == ComputeBackend.Vulkan && !Regex.IsMatch(result.Error, @"(?i)(using Vulkan|ggml_vulkan:.*device|Vulkan\d+)"))
                        throw new InvalidOperationException("Vulkan 引擎没有报告 GPU 初始化成功。");
                    var match = Regex.Match(result.Error, @"(?im)(?:Device\s+\d+|device\s+\d+)\s*:\s*([^\r\n]+)");
                    if (detectedDevice.Length > 0) device = detectedDevice;
                    else if (match.Success) device = match.Groups[1].Value.Trim();
                    if (device.Length == 0) device = backend.ToString();
                    using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output + ".json", ct));
                    segments = [];
                    foreach (var s in json.RootElement.GetProperty("transcription").EnumerateArray())
                    {
                        var offsets = s.GetProperty("offsets");
                        var from = offsets.GetProperty("from").GetDouble() / 1000 + offset;
                        var to = Math.Min(offsets.GetProperty("to").GetDouble() / 1000 + offset, finish);
                        var text = ChineseTypography.Apply(s.GetProperty("text").GetString()?.Trim() ?? "");
                        if (text.Length > 0 && to > from) segments.Add(new() { Start = from, End = to, Text = text, Words = SentenceSegmentation.ReadWhisperWords(s, offset, finish) });
                    }
                    if (request.Settings.AutoSegmentEnglish && segments.Any(s => SentenceSegmentation.IsEnglish(s.Text) && SentenceSegmentation.HasReliableWords(s)))
                        segments = SentenceSegmentation.Optimize(segments, await media.DetectSilencesAsync(wav, ct, offset));
                    var pending = checkpoint + ".pending";
                    await File.WriteAllTextAsync(pending, Json.Write(segments), ct); File.Move(pending, checkpoint, true);
                    File.Delete(wav);
                }
                all.AddRange(segments);
                await progress(new("segments", "字幕已保存到检查点", (double)(chunk + 1) / chunkCount, Segments: Subtitles.Repair(all).ToArray(), Backend: backend.ToString(), Device: device));
            }
            watch.Stop();
            await progress(new("completed", "转写完成", 1, Segments: Subtitles.Repair(all).ToArray(), Backend: backend.ToString(), Device: device, ElapsedSeconds: watch.Elapsed.TotalSeconds, RealTimeFactor: watch.Elapsed.TotalSeconds / (finish - begin)));
        }
    }
}
