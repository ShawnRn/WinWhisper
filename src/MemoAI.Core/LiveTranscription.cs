using SherpaOnnx;

namespace MemoAI.Core;

public sealed record LiveAudioClip(string Path, double Offset);

public sealed class LiveTranscription(AppPaths paths)
{
    public async Task RunAsync(JobRequest request, TextReader input, Func<JobEvent, Task> progress, CancellationToken ct)
    {
        var onnx = AsrModels.IsManifest(request.Settings.TranscriptionModelPath);
        await progress(new("progress", "加载录音转写模型…", Backend: onnx ? "Pending" : null));
        // ONNX recognizer stays resident between clips; whisper.cpp uses its isolated CLI per clip.
        using var recognizer = onnx ? new SherpaAsrSession(request.Settings.TranscriptionModelPath, request.Settings, request.WorkDirectory) : null;
        var index = 0;
        while (await input.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested(); var clip = Json.Read<LiveAudioClip>(line);
            if (!double.IsFinite(clip.Offset) || clip.Offset < 0) throw new InvalidDataException("实时录音片段时间无效。");
            await progress(new("progress", $"正在识别录音片段 {++index}", Backend: recognizer?.Backend));
            if (recognizer is not null)
            {
                var samples = SherpaTranscription.ReadPcm16(clip.Path); var segments = new List<SubtitleSegment>();
                foreach (var (start, end) in SherpaTranscription.AudioRanges(samples, request.Settings.EnableVad))
                {
                    var result = recognizer.Decode(samples[start..end]);
                    var text = ChineseTypography.Apply(System.Text.RegularExpressions.Regex.Replace(result.Text, @"<\|[^|]*\|>", "").Trim());
                    if (text.Length > 0) segments.Add(new() { Start = clip.Offset + start / 16000d, End = clip.Offset + end / 16000d, Text = text });
                }
                await progress(new("segments", $"已识别至 {TimeSpan.FromSeconds(clip.Offset + samples.Length / 16000d):hh\\:mm\\:ss}", Segments: segments.ToArray(), Backend: recognizer.Backend, Device: recognizer.Device));
            }
            else
            {
                if (request.Settings.EnableVad && SherpaTranscription.AudioRanges(SherpaTranscription.ReadPcm16(clip.Path), true).Count == 0)
                {
                    await progress(new("clip-completed", clip.Path)); continue;
                }
                var chunk = request with { MediaPath = clip.Path, Operation = "transcribe", WorkDirectory = Path.Combine(request.WorkDirectory, "live", index.ToString("00000")) };
                await new WhisperEngine(paths).TranscribeAsync(chunk, async e =>
                {
                    if (e.Type == "completed") await progress(e with { Type = "segments", Segments = e.Segments?.Select(s => { var copy = s.Copy(); copy.Start += clip.Offset; copy.End += clip.Offset; return copy; }).ToArray() });
                    else if (e.Type != "segments") await progress(e);
                }, ct);
            }
            await progress(new("clip-completed", clip.Path));
        }
        await progress(new("completed", "录音字幕已保存", 1, Backend: recognizer?.Backend, Device: recognizer?.Device));
    }
}
