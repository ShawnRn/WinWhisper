using System.Text.RegularExpressions;
using System.Text.Json;

namespace MemoAI.Core;

public sealed record AudioSilence(double Start, double End);

public static class SentenceSegmentation
{
    private static readonly Regex Lexeme = new(@"[\p{L}\p{N}]+(?:[’'][\p{L}\p{N}]+)*", RegexOptions.Compiled);
    private static string Normalize(string text) => Regex.Replace(ChineseTypography.Apply(text), @"\s+", " ").Trim();
    public static bool HasReliableWords(SubtitleSegment cue) => cue.Words.Count > 0
        && Normalize(string.Concat(cue.Words.Select(w => w.Text))) == Normalize(cue.Text)
        && cue.Words.All(w => double.IsFinite(w.Start) && double.IsFinite(w.End) && w.End > w.Start && w.Start >= cue.Start - .1 && w.End <= cue.End + .1)
        && cue.Words.Zip(cue.Words.Skip(1)).All(p => p.Second.Start >= p.First.End - .02);
    public static bool IsEnglish(string text) => Regex.IsMatch(text, "[A-Za-z]") && !Regex.IsMatch(text, @"[\u3400-\u9fff\u3040-\u30ff\uac00-\ud7af]");
    public static List<TimedWord> ReadOnnxWords(string[] tokens, float[]? timestamps, float[]? durations, double offset, double finish)
    {
        if (timestamps is null || durations is null || tokens.Length != timestamps.Length || tokens.Length != durations.Length) return [];
        var words = new List<TimedWord>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var text = tokens[i].Replace('▁', ' '); var start = offset + timestamps[i]; var end = Math.Min(finish, start + durations[i]);
            if (!double.IsFinite(start) || !double.IsFinite(end) || start < offset || end < start) return [];
            if (words.Count > 0 && text.Length > 0 && !char.IsWhiteSpace(text[0]))
            { var last = words[^1]; words[^1] = last with { Text = last.Text + text, End = Math.Max(last.End, end) }; }
            else words.Add(new(text, start, end));
        }
        // TDT can assign a zero duration to a continuation piece (e.g. the y
        // in country). Merge the BPE pieces first; the complete word must still
        // have a positive duration. Never invent an end for a zero-length word.
        words = JoinEnglishSuffixes(words);
        return words.Any(w => w.End <= w.Start) ? [] : words.Select(w => w with { Text = ChineseTypography.Apply(w.Text) }).ToList();
    }

    // Full JSON contains BPE pieces. Join pieces into words before using their
    // timestamps; never invent word timings by dividing cue duration by length.
    public static List<TimedWord> ReadWhisperWords(JsonElement cue, double offset, double finish)
    {
        if (!cue.TryGetProperty("tokens", out var tokens)) return [];
        var words = new List<TimedWord>();
        foreach (var token in tokens.EnumerateArray())
        {
            if (token.TryGetProperty("id", out var id) && id.GetInt32() >= 50256) continue;
            var text = token.GetProperty("text").GetString() ?? "";
            if (text.Length == 0 || !token.TryGetProperty("offsets", out var times)) continue;
            var start = offset + times.GetProperty("from").GetDouble() / 1000;
            var end = Math.Min(finish, offset + times.GetProperty("to").GetDouble() / 1000);
            if (start < offset || end < start) return [];
            if (words.Count > 0 && !char.IsWhiteSpace(text[0]))
            { var previous = words[^1]; words[^1] = previous with { Text = previous.Text + text, End = Math.Max(previous.End, end) }; }
            else words.Add(new(text, start, end));
        }
        return JoinEnglishSuffixes(words).Select(w => w with { Text = ChineseTypography.Apply(w.Text) }).ToList();
    }

    private static List<TimedWord> JoinEnglishSuffixes(List<TimedWord> words)
    {
        var result = new List<TimedWord>();
        foreach (var word in words)
        {
            if (result.Count > 0 &&
                (Regex.IsMatch(result[^1].Text, @"[A-Za-z][’‘']\s*$") && Regex.IsMatch(word.Text, @"^\s+(?:s|t|re|ve|ll|d|m)\b", RegexOptions.IgnoreCase)
                 || Regex.IsMatch(result[^1].Text, @"[A-Za-z]$") && Regex.IsMatch(word.Text, @"^\s+[’‘']\s*(?:s|t|re|ve|ll|d|m)\b", RegexOptions.IgnoreCase)))
            {
                var previous = result[^1];
                result[^1] = previous with { Text = ChineseTypography.Apply(previous.Text + word.Text), End = Math.Max(previous.End, word.End) };
            }
            else result.Add(word);
        }
        return result;
    }

    public static List<SubtitleSegment> Optimize(IEnumerable<SubtitleSegment> source, IReadOnlyList<AudioSilence>? silences = null)
    {
        var output = new List<SubtitleSegment>(); var group = new List<SubtitleSegment>();
        void Flush()
        {
            if (group.Count == 0) return;
            var words = group.SelectMany(c => c.Words).ToArray();
            var first = 0;
            while (first < words.Length)
            {
                var best = first; var bestScore = double.NegativeInfinity; var chars = 0;
                for (var i = first; i < words.Length; i++)
                {
                    chars += words[i].Text.Length; var duration = words[i].End - words[first].Start;
                    if (i > first && (chars > 112 || duration > 9)) break;
                    var last = i == words.Length - 1;
                    var gap = last ? 0 : words[i + 1].Start - words[i].End;
                    var natural = EndsSentence(words[i].Text);
                    var pause = gap >= .45 || silences?.Any(s => s.End - s.Start >= .3 && s.Start <= words[i].End + .15 && s.End >= words[i].End && (last || s.End <= words[i + 1].Start + .15)) == true;
                    var score = (last ? 110 : natural ? 100 : pause ? 65 : Regex.IsMatch(words[i].Text, "[,;:]$|[，；：]$") ? 35 : 0) + Math.Min(duration, 5) * 2;
                    if (duration < 1.2 && !last) score -= 60;
                    if (Regex.IsMatch(words[i].Text, @"\b(?:a|an|the|of|to|and|or|at|in|for|with|my|your|our|is|are|was|were)$", RegexOptions.IgnoreCase)) score -= 45;
                    if (score > bestScore) { bestScore = score; best = i; }
                    if ((natural || gap >= .8) && duration >= 1.2) break;
                }
                var selected = words[first..(best + 1)];
                output.Add(new() { Start = selected[0].Start, End = selected[^1].End, Text = ChineseTypography.Apply(string.Concat(selected.Select(w => w.Text)).Trim()), Speaker = group[0].Speaker, Words = [..selected] });
                first = best + 1;
            }
            group.Clear();
        }
        foreach (var cue in source.OrderBy(c => c.Start))
        {
            if (!HasReliableWords(cue) || !IsEnglish(cue.Text) || cue.Translation.Length > 0)
            { Flush(); var copy = cue.Copy(); copy.Text = ChineseTypography.Apply(copy.Text); output.Add(copy); continue; }
            if (group.Count > 0 && (cue.Start - group[^1].End > 1.2 || cue.Speaker != group[^1].Speaker || cue.Words[0].Start < group[^1].Words[^1].End)) Flush();
            group.Add(cue);
        }
        Flush(); return output;
    }
    private static bool EndsSentence(string text)
    {
        text = text.Trim().TrimEnd('”', '’', '"', ')');
        if (text.EndsWith('!') || text.EndsWith('?')) return true;
        return text.EndsWith('.') && !Regex.IsMatch(text.Trim(), @"(?:\b(?:Mr|Mrs|Ms|Dr|Prof|Jr|Sr|St|vs|etc)|\b[A-Za-z]|(?:\b[A-Za-z]\.)+[A-Za-z])\.$", RegexOptions.IgnoreCase);
    }

    // Accept punctuation/case edits only. IDs, numbers, spelling and word order
    // must remain identical. Mapping uses original character/word boundaries.
    public static SubtitleSegment RestorePunctuation(SubtitleSegment cue, string proposed)
    {
        var before = Lexeme.Matches(cue.Text).Cast<Match>().ToArray();
        var after = Lexeme.Matches(proposed).Cast<Match>().ToArray();
        if (before.Length == 0 || before.Length != after.Length || before.Where((m, i) => !m.Value.Replace('’', '\'').Equals(after[i].Value.Replace('’', '\''), StringComparison.OrdinalIgnoreCase)).Any())
            throw new InvalidDataException("标点模型改动了单词或数字，已拒绝覆盖原文。");
        if (proposed.Length > cue.Text.Length * 1.6 + 32 || Regex.IsMatch(proposed, @"[<>`{}\[\]]")) throw new InvalidDataException("标点模型返回了额外结构，已保留原文。");
        var copy = cue.Copy(); copy.Text = ChineseTypography.Apply(proposed.Trim());
        if (HasReliableWords(cue))
        {
            var original = string.Concat(cue.Words.Select(w => w.Text)); var oldMatches = Lexeme.Matches(original).Cast<Match>().ToArray();
            var position = 0; var wordIndex = 0; var mapped = new List<TimedWord>(); var lastCut = 0;
            foreach (var w in cue.Words)
            {
                position += w.Text.Length;
                while (wordIndex < oldMatches.Length && oldMatches[wordIndex].Index + oldMatches[wordIndex].Length <= position) wordIndex++;
                if (wordIndex == 0) { mapped.Add(w); continue; }
                var end = wordIndex < after.Length ? after[wordIndex].Index : proposed.Length;
                mapped.Add(w with { Text = ChineseTypography.Apply(proposed[lastCut..end]) }); lastCut = end;
            }
            copy.Words = mapped;
            if (!HasReliableWords(copy)) copy.Words = []; // Fall back to original cue boundaries.
        }
        return copy;
    }
}
