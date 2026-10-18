using System.Text.RegularExpressions;

namespace MemoAI.Core;

public static class ChineseTypography
{
    public const string Instructions = "排版要求：所有中文引号使用直角引号「」；书籍、电影、游戏、报告、节目名称使用《》；中文与英文之间、中文与数字之间添加一个半角空格；中文句子使用全角标点，中文逗号使用「，」。英语缩写和所有格的撇号使用 ’，英语引文使用弯引号 ‘ ’ 或 “ ”。保留数字分隔符、代码、网址、路径、数学表达式和数据格式的原始内容。";
    public static string Apply(string text)
    {
        if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
        { try { using var json = System.Text.Json.JsonDocument.Parse(text); return text; } catch (System.Text.Json.JsonException) { } }
        // Keep fenced/inline code, Markdown link destinations and URLs byte-for-byte.
        var result = new System.Text.StringBuilder(); var offset = 0;
        foreach (Match match in Regex.Matches(text, @"(?s)```.*?(?:```|\z)|~~~.*?(?:~~~|\z)|`[^`\n]*`|!?\[[^\]\n]*\]\([^\)\n]+\)|https?://[^\s<>]+|(?<!\w)[A-Za-z]:\\[^\s]+|\$[^$\n]+\$"))
        {
            result.Append(Plain(text[offset..match.Index])); result.Append(match.Value); offset = match.Index + match.Length;
        }
        result.Append(Plain(text[offset..])); return result.ToString();
        static string Plain(string value)
        {
            value = Regex.Replace(value, "“([^”\\r\\n]*[\u3400-\u9fff][^”\\r\\n]*)”|‘([^’\\r\\n]*[\u3400-\u9fff][^’\\r\\n]*)’", match => "「" + (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) + "」");
            value = Regex.Replace(value, "\"([^\"\r\n]*[\u3400-\u9fff][^\"\r\n]*)\"", "「$1」");
            // Apostrophes first: contractions, possessives and leading elisions.
            value = Regex.Replace(value, @"(?<=[A-Za-z])['‘](?=[A-Za-z])|'(?=(?:tis|twas|em|cause|round|til|\d{2}s)\b)", "’", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "\"([^\"\\r\\n]+)\"", "“$1”");
            value = Regex.Replace(value, "(?<![A-Za-z0-9])'([^'\\r\\n]+)'", m => Regex.IsMatch(m.Groups[1].Value, "[\u3400-\u9fff]") ? "「" + m.Groups[1].Value + "」" : "‘" + m.Groups[1].Value + "’");
            value = Regex.Replace(value, @"(?<=[sS])'(?=\s|[.,;:!?)]|$)", "’");
            value = Regex.Replace(value, @"(?<=[\u3400-\u9fff」》])[ \t]*,[ \t]*|,[ \t]*(?=[\u3400-\u9fff「《])", "，");
            value = Regex.Replace(value, @"(?<=[\u3400-\u9fff])(?=(?:\*{1,3}|_{1,3}|~~)[A-Za-z0-9])", " ");
            value = Regex.Replace(value, @"(?<=[A-Za-z0-9])((?:\*{1,3}|_{1,3}|~~))(?=[\u3400-\u9fff])", "$1 ");
            return Regex.Replace(value, @"(?<=[\u3400-\u9fff])(?=[A-Za-z0-9])|(?<=[A-Za-z0-9])(?=[\u3400-\u9fff])", " ");
        }
    }
}
