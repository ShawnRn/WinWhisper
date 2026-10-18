using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MemoAI.Core;

public static partial class Subtitles
{
    [GeneratedRegex(@"(?<start>\d{1,2}:\d{2}:\d{2}[,.]\d{1,3}|\d{1,2}:\d{2}[,.]\d{1,3})\s*-->\s*(?<end>\d{1,2}:\d{2}:\d{2}[,.]\d{1,3}|\d{1,2}:\d{2}[,.]\d{1,3})[^\n]*\n(?<text>.*?)(?=\n\s*\n|\z)", RegexOptions.Singleline)]
    private static partial Regex CueRegex();
    public static List<SubtitleSegment> Parse(string content, bool bilingual = false)
    {
        var result = new List<SubtitleSegment>();
        content = content.Replace("\r\n", "\n").TrimStart('\uFEFF');
        foreach (Match match in CueRegex().Matches(content))
        {
            var start = ParseTime(match.Groups["start"].Value); var end = ParseTime(match.Groups["end"].Value);
            if (end <= start) continue;
            var lines = match.Groups["text"].Value.Trim().Split('\n');
            result.Add(new() { Start = start, End = end, Text = bilingual ? lines[0] : string.Join("\n", lines), Translation = bilingual ? string.Join("\n", lines.Skip(1)) : "" });
        }
        return result;
    }
    public static double ParseTime(string s)
    {
        var pieces = s.Replace(',', '.').Split(':'); double value = 0;
        foreach (var piece in pieces) value = value * 60 + double.Parse(piece, CultureInfo.InvariantCulture);
        if (!double.IsFinite(value) || value < 0) throw new FormatException("字幕时间戳无效。");
        return value;
    }
    public static string FormatTime(double seconds, bool vtt = false)
    {
        var t = TimeSpan.FromMilliseconds(Math.Round(Math.Max(0, seconds) * 1000));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{(vtt ? '.' : ',')}{t.Milliseconds:000}";
    }
    private static string Content(SubtitleSegment s, bool bilingual) => (string.IsNullOrWhiteSpace(s.Speaker) ? "" : $"[{s.Speaker}] ") + s.Text + (bilingual && !string.IsNullOrWhiteSpace(s.Translation) ? "\n" + s.Translation : "");
    public static string Export(MediaSession session, string extension, bool bilingual = true)
        => Export(session, extension, bilingual ? SubtitleTextMode.Bilingual : SubtitleTextMode.Original);
    public static string Export(MediaSession session, string extension, SubtitleTextMode mode)
    {
        var builder = new StringBuilder();
        var segments = session.Segments.OrderBy(s => s.Start).ToArray();
        if (mode == SubtitleTextMode.Translation && segments.Any(s => string.IsNullOrWhiteSpace(s.Translation)))
            throw new InvalidOperationException("部分字幕尚无译文，请先完成翻译再导出纯译文。");
        string Content(SubtitleSegment cue, bool _) => (string.IsNullOrWhiteSpace(cue.Speaker) ? "" : $"[{cue.Speaker}] ")
            + ChineseTypography.Apply(mode == SubtitleTextMode.Translation ? cue.Translation : cue.Text)
            + (mode == SubtitleTextMode.Bilingual && !string.IsNullOrWhiteSpace(cue.Translation) ? "\n" + ChineseTypography.Apply(cue.Translation) : "");
        var bilingual = mode == SubtitleTextMode.Bilingual;
        switch (extension.TrimStart('.').ToLowerInvariant())
        {
            case "srt": case "vtt":
                var vtt = extension.TrimStart('.').Equals("vtt", StringComparison.OrdinalIgnoreCase);
                if (vtt) builder.AppendLine("WEBVTT\n");
                for (var i = 0; i < segments.Length; i++)
                {
                    var s = segments[i]; builder.AppendLine((i + 1).ToString());
                    builder.AppendLine($"{FormatTime(s.Start, vtt)} --> {FormatTime(s.End, vtt)}");
                    builder.AppendLine(Content(s, bilingual)); builder.AppendLine();
                }
                break;
            case "ass":
                builder.AppendLine("[Script Info]\nScriptType: v4.00+\nPlayResX: 1920\nPlayResY: 1080\nWrapStyle: 0\n\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\nStyle: Default,Microsoft YaHei UI,48,&H00FFFFFF,&H000000FF,&H00202020,&H80000000,0,0,0,0,100,100,0,0,1,2,0,2,50,50,48,1\n\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
                foreach (var s in segments) builder.AppendLine($"Dialogue: 0,{AssTime(s.Start)},{AssTime(s.End)},Default,,0,0,0,,{EscapeAss(Content(s, bilingual))}");
                break;
            case "md": case "markdown":
                builder.AppendLine($"# {session.Title}\n");
                if (session.Summary.Length > 0) builder.AppendLine($"## 总结\n\n{session.Summary}\n");
                if (session.Chapters.Length > 0) builder.AppendLine($"## 章节\n\n{session.Chapters}\n");
                builder.AppendLine("## 转写\n");
                foreach (var s in segments) builder.AppendLine($"**{FormatTime(s.Start, true)}** {Content(s, bilingual)}\n");
                break;
            case "txt": foreach (var s in segments) builder.AppendLine(Content(s, bilingual)); break;
            case "json": return Json.Write(session);
            default: throw new NotSupportedException($"不支持的导出格式：{extension}");
        }
        return builder.ToString();
    }
    private static string AssTime(double s)
    {
        var t = TimeSpan.FromMilliseconds(Math.Round(Math.Max(0, s) * 100) * 10);
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
    }
    private static string EscapeAss(string text) => text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("\r", "").Replace("\n", "\\N");
    public static List<SubtitleSegment> Repair(IEnumerable<SubtitleSegment> source)
    {
        var result = new List<SubtitleSegment>();
        foreach (var item in source.OrderBy(s => s.Start))
        {
            if (!double.IsFinite(item.Start) || !double.IsFinite(item.End) || item.End <= item.Start || string.IsNullOrWhiteSpace(item.Text)) continue;
            var s = item.Copy(); s.Text = s.Text.Trim();
            var last = result.LastOrDefault();
            if (last is not null && last.Text == s.Text && s.Start - last.End <= 0.5) { last.End = Math.Max(last.End, s.End); continue; }
            if (last is not null && s.Start < last.End) s.Start = last.End;
            if (s.End > s.Start) result.Add(s);
        }
        return result;
    }
    public static (SubtitleSegment Left, SubtitleSegment Right) Split(SubtitleSegment s, double position)
    {
        if (position <= s.Start || position >= s.End) throw new ArgumentOutOfRangeException(nameof(position), "拆分位置必须在字幕内部。");
        var fraction = (position - s.Start) / (s.End - s.Start);
        var cut = Math.Clamp((int)Math.Round(s.Text.Length * fraction), 1, Math.Max(1, s.Text.Length - 1));
        var tcut = Math.Clamp((int)Math.Round(s.Translation.Length * fraction), 0, s.Translation.Length);
        return (new() { Start = s.Start, End = position, Text = s.Text[..Math.Min(cut, s.Text.Length)], Translation = s.Translation[..tcut], Speaker = s.Speaker },
            new() { Start = position, End = s.End, Text = s.Text[Math.Min(cut, s.Text.Length)..], Translation = s.Translation[tcut..], Speaker = s.Speaker });
    }
    public static SubtitleSegment Merge(SubtitleSegment a, SubtitleSegment b) => new()
    {
        Start = Math.Min(a.Start, b.Start), End = Math.Max(a.End, b.End), Text = $"{a.Text.Trim()} {b.Text.Trim()}",
        Translation = $"{a.Translation.Trim()} {b.Translation.Trim()}".Trim(), Speaker = a.Speaker
    };
    public static string MindMapSvg(string json, string title)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var nodes = new List<(int depth, string text)>();
        void Walk(System.Text.Json.JsonElement node, int depth)
        {
            if (depth > 8 || nodes.Count >= 150) return;
            nodes.Add((depth, node.TryGetProperty("title", out var t) ? t.GetString() ?? "" : ""));
            if (node.TryGetProperty("children", out var children) && children.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var child in children.EnumerateArray()) Walk(child, depth + 1);
        }
        Walk(document.RootElement, 0);
        var width = 1400; var height = Math.Max(240, nodes.Count * 58 + 120);
        var b = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><rect width=\"100%\" height=\"100%\" fill=\"#f6f8fb\"/><title>{WebUtility.HtmlEncode(title)}</title>");
        var ancestors = new Dictionary<int, int>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var (depth, text) = nodes[i]; var x = 36 + depth * 100; var y = 36 + i * 58;
            if (depth > 0 && ancestors.TryGetValue(depth - 1, out var parent))
                b.Append($"<path d=\"M {x - 90} {36 + parent * 58 + 22} V {y + 22} H {x}\" fill=\"none\" stroke=\"#a7b5c9\" stroke-width=\"2\"/>");
            ancestors[depth] = i;
            b.Append($"<rect x=\"{x}\" y=\"{y}\" width=\"{width - x - 36}\" height=\"44\" rx=\"10\" fill=\"{(depth == 0 ? "#315dce" : "#ffffff")}\"/><text x=\"{x + 16}\" y=\"{y + 28}\" font-family=\"Segoe UI,Microsoft YaHei,sans-serif\" font-size=\"16\" fill=\"{(depth == 0 ? "#ffffff" : "#243449")}\">{WebUtility.HtmlEncode(text)}</text>");
        }
        return b.Append("</svg>").ToString();
    }
}
