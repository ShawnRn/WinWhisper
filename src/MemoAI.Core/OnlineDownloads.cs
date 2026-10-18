using System.Text.RegularExpressions;

namespace MemoAI.Core;

public sealed record OnlineDownloadOptions(string Browser = "", string Profile = "", string CookieFile = "", string Proxy = "", int Quality = 0);

public static class OnlineDownloads
{
    private static readonly string[] browsers = ["edge", "chrome", "firefox", "brave", "vivaldi", "opera", "chromium"];
    public static List<string> Arguments(string url, string directory, string ffmpegDirectory, string deno, OnlineDownloadOptions options, string? temporaryCookies = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new ArgumentException("请输入有效的 HTTP/HTTPS 媒体链接。");
        if (options.Browser.Length > 0 && !browsers.Contains(options.Browser)) throw new ArgumentException("不支持的浏览器。");
        if (options.Profile.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new ArgumentException("浏览器资料名称无效。");
        if (options.Quality is < 0 or > 3) throw new ArgumentException("不支持的下载质量。");
        var args = new List<string> { "--ignore-config", "--no-cookies", "--no-cookies-from-browser", "--no-cache-dir", "--no-playlist", "--no-overwrites", "--newline", "--no-simulate", "--progress", "--windows-filenames", "--ffmpeg-location", ffmpegDirectory, "--js-runtimes", "deno:" + deno, "--merge-output-format", "mp4", "--print", "after_move:filepath", "-o", Path.Combine(directory, "%(title).150B [%(id)s].%(ext)s") };
        if (temporaryCookies is not null) args.AddRange(["--cookies", temporaryCookies]);
        else if (options.Browser.Length > 0) args.AddRange(["--cookies-from-browser", options.Browser + (options.Profile.Trim().Length > 0 ? ":" + options.Profile.Trim() : "")]);
        args.AddRange(["--socket-timeout", "30", "--retries", "3", "--extractor-retries", "2"]);
        if (options.Proxy.Length > 0)
        {
            if (!Uri.TryCreate(options.Proxy, UriKind.Absolute, out var proxy) || proxy.Scheme is not ("http" or "https" or "socks5" or "socks5h")) throw new ArgumentException("代理地址应使用 http、https 或 socks5 格式。");
            args.AddRange(["--proxy", options.Proxy]);
        }
        args.AddRange(["-f", options.Quality switch { 1 => "bv*[height<=720]+ba/b[height<=720]/b", 2 => "bv*+ba/b", 3 => "ba/b", _ => "bv*[height<=1080]+ba/b[height<=1080]/b" }, "--", uri.AbsoluteUri]);
        return args;
    }
    public static string? ProgressMessage(string line)
    {
        if (line.Contains("cookie", StringComparison.OrdinalIgnoreCase)) return "正在读取选定的登录状态…";
        if (line.StartsWith("[download]", StringComparison.Ordinal))
        {
            var match = Regex.Match(line, @"\[download\]\s+([\d.]+%.*)");
            return match.Success && !match.Value.Contains("http", StringComparison.OrdinalIgnoreCase) ? "下载中 · " + match.Groups[1].Value : "正在下载媒体…";
        }
        if (line.StartsWith("[Merger]", StringComparison.Ordinal) || line.StartsWith("[ExtractAudio]", StringComparison.Ordinal)) return "正在合并并整理媒体…";
        if (line.StartsWith("[youtube", StringComparison.Ordinal)) return "正在解析 YouTube 媒体…";
        return null;
    }
    public static string FailureMessage(string error)
    {
        if (Regex.IsMatch(error, "(?i)DPAPI|decrypt|app.bound|could not copy.*cookie|permission denied.*cookie|cookie.*locked"))
            return "无法读取浏览器 Cookie。可先关闭该浏览器后重试；若仍受浏览器加密保护，请选择 Firefox 或手动提供 Netscape 格式的 Cookie 文件。";
        if (Regex.IsMatch(error, "(?i)sign in|login|authenticate|not a bot|cookies")) return "该媒体需要登录状态或站点验证。请在在线导入页选择已登录的浏览器，或提供 Cookie 文件。";
        if (Regex.IsMatch(error, "(?i)429|too many requests")) return "媒体来源限制了请求频率，请稍后重试。";
        if (Regex.IsMatch(error, "(?i)403|forbidden")) return "媒体来源拒绝了下载请求，请检查登录状态、代理或更换下载质量。";
        if (Regex.IsMatch(error, "(?i)timed out|network|connection|proxy|resolve")) return "下载连接失败，请检查系统代理或在在线导入页填写代理地址。";
        if (Regex.IsMatch(error, "(?i)not available|unavailable|private video|removed")) return "媒体不可用，可能已删除、设为私密或受到地区限制。";
        return "在线媒体下载失败。请检查链接、网络和登录状态，或更新随附的 yt-dlp 引擎后重试。";
    }
}
