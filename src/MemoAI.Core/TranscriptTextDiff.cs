using System.Globalization;
using System.Text;

namespace MemoAI.Core;

public sealed record TranscriptTextPart(string Text, bool Changed);
public sealed record TranscriptTextComparison(IReadOnlyList<TranscriptTextPart> Before, IReadOnlyList<TranscriptTextPart> After);

public static class TranscriptTextDiff
{
    // Latin words remain readable units; CJK and emoji retain grapheme boundaries.
    // Bound the LCS allocation for unusually large imported cues.
    public static TranscriptTextComparison Compare(string before, string after)
    {
        var a = Tokens(before); var b = Tokens(after); var left = new List<TranscriptTextPart>(); var right = new List<TranscriptTextPart>();
        var prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix]) { Add(left, a[prefix], false); Add(right, b[prefix++], false); }
        var ae = a.Count; var be = b.Count;
        while (ae > prefix && be > prefix && a[ae - 1] == b[be - 1]) { ae--; be--; }
        var n = ae - prefix; var m = be - prefix;
        if ((long)n * m > 4_000_000)
        {
            for (var i = prefix; i < ae; i++) Add(left, a[i], true);
            for (var j = prefix; j < be; j++) Add(right, b[j], true);
        }
        else
        {
            var scores = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    scores[i, j] = a[prefix + i] == b[prefix + j] ? scores[i + 1, j + 1] + 1 : Math.Max(scores[i + 1, j], scores[i, j + 1]);
            var x = 0; var y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[prefix + x] == b[prefix + y]) { Add(left, a[prefix + x++], false); Add(right, b[prefix + y++], false); }
                else if (x < n && (y == m || scores[x + 1, y] >= scores[x, y + 1])) Add(left, a[prefix + x++], true);
                else Add(right, b[prefix + y++], true);
            }
        }
        for (var i = ae; i < a.Count; i++) Add(left, a[i], false);
        for (var j = be; j < b.Count; j++) Add(right, b[j], false);
        return new(Coalesce(left), Coalesce(right));
    }
    private static void Add(List<TranscriptTextPart> parts, string text, bool changed)
    {
        if (text.Length == 0) return;
        parts.Add(new(text, changed));
    }
    private static List<TranscriptTextPart> Coalesce(List<TranscriptTextPart> parts)
    {
        var result = new List<TranscriptTextPart>(); var text = new StringBuilder(); var changed = false;
        foreach (var part in parts)
        {
            if (text.Length > 0 && changed != part.Changed) { result.Add(new(text.ToString(), changed)); text.Clear(); }
            text.Append(part.Text); changed = part.Changed;
        }
        if (text.Length > 0) result.Add(new(text.ToString(), changed));
        return result;
    }
    private static List<string> Tokens(string text)
    {
        var tokens = new List<string>(); var run = new StringBuilder(); var previousKind = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var kind = element.Length == 1 && (char.IsAsciiLetterOrDigit(element[0]) || element[0] == '_') ? 1 : string.IsNullOrWhiteSpace(element) ? 2 : 0;
            if (run.Length > 0 && (kind != previousKind || kind == 0)) { tokens.Add(run.ToString()); run.Clear(); }
            run.Append(element); previousKind = kind;
        }
        if (run.Length > 0) tokens.Add(run.ToString());
        return tokens;
    }
}
