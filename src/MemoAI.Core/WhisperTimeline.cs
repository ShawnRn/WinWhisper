using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemoAI.Core;

// b5454 CLI full JSON can contain VAD-compressed token times alongside original
// segment times. Recover only from the engine's complete, measured speech map;
// never stretch text to fit a cue or infer an offset from its first word.
public static class WhisperTimeline
{
    public static List<SubtitleSegment> ReadSegments(JsonElement json, double offset, double finish, string engineLog)
    {
        List<SubtitleSegment> Read(Func<double, double>? timeline) => json.GetProperty("transcription").EnumerateArray().Select(c =>
        {
            var times = c.GetProperty("offsets");
            return new SubtitleSegment
            {
                Start = offset + times.GetProperty("from").GetDouble() / 1000,
                End = Math.Min(finish, offset + times.GetProperty("to").GetDouble() / 1000),
                Text = ChineseTypography.Apply(c.GetProperty("text").GetString()?.Trim() ?? ""),
                Words = SentenceSegmentation.ReadWhisperWords(c, offset, finish, timeline)
            };
        }).Where(c => c.Text.Length > 0 && c.End > c.Start).ToList();

        var raw = Read(null);
        var map = ReadVadMap(engineLog);
        if (map is null || raw.All(SentenceSegmentation.HasReliableWords)) return raw;
        var mapped = Read(map);
        // Select one timeline for the whole chunk, avoiding double mapping when
        // an engine already emits original token times. Invalid cues still fall
        // back to original boundaries in SentenceSegmentation.
        return mapped.Count(SentenceSegmentation.HasReliableWords) > raw.Count(SentenceSegmentation.HasReliableWords) ? mapped : raw;
    }

    private sealed record Span(long OriginalStart, long OriginalEnd, long ProcessedStart, long ProcessedEnd);
    private static Func<double, double>? ReadVadMap(string log)
    {
        var count = Regex.Match(log, @"whisper_vad: detected (\d+) speech segments");
        var rows = Regex.Matches(log, @"vad_segment_info: orig_start: ([0-9.]+), orig_end: ([0-9.]+), vad_start: ([0-9.]+), vad_end: ([0-9.]+)");
        if (!count.Success || !int.TryParse(count.Groups[1].Value, out var expected) || expected == 0 || rows.Count != expected) return null;
        var spans = new List<Span>();
        foreach (Match row in rows)
        {
            var times = new long[4];
            for (var i = 0; i < times.Length; i++)
            {
                if (!double.TryParse(row.Groups[i + 1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var time) || !double.IsFinite(time) || time > int.MaxValue) return null;
                times[i] = (long)Math.Round(time * 100);
            }
            var span = new Span(times[0], times[1], times[2], times[3]);
            if (span.OriginalEnd <= span.OriginalStart || span.ProcessedEnd <= span.ProcessedStart
                || Math.Abs((span.OriginalEnd - span.OriginalStart) - (span.ProcessedEnd - span.ProcessedStart)) > 2
                || spans.Count > 0 && (span.OriginalStart < spans[^1].OriginalEnd || span.ProcessedStart <= spans[^1].ProcessedEnd)) return null;
            spans.Add(span);
        }
        return seconds =>
        {
            var t = (long)Math.Round(seconds * 100);
            if (t <= spans[0].ProcessedStart) return spans[0].OriginalStart / 100d;
            if (t >= spans[^1].ProcessedEnd) return spans[^1].OriginalEnd / 100d;
            for (var i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                if (t >= span.ProcessedStart && t <= span.ProcessedEnd)
                    return (span.OriginalStart + (t - span.ProcessedStart) * (span.OriginalEnd - span.OriginalStart) / (span.ProcessedEnd - span.ProcessedStart)) / 100d;
                if (i + 1 < spans.Count && t > span.ProcessedEnd && t < spans[i + 1].ProcessedStart)
                    return (t <= (span.ProcessedEnd + spans[i + 1].ProcessedStart) / 2 ? span.OriginalEnd : spans[i + 1].OriginalStart) / 100d;
            }
            return seconds;
        };
    }
}
