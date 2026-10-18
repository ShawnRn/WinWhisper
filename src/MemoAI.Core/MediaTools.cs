using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MemoAI.Core;

public sealed class MediaTools(AppPaths paths)
{
    public string Ffmpeg => paths.FindEngine("ffmpeg", "ffmpeg.exe");
    public async Task<double> ProbeAsync(string file, CancellationToken ct)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("媒体文件已移动或不存在。", file);
        var result = await Processes.RunAsync(paths.FindEngine("ffmpeg", "ffprobe.exe"), ["-v", "error", "-show_entries", "format=duration", "-of", "json", file], ct: ct);
        Processes.EnsureSuccess(result, "读取媒体信息");
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var duration)
            ? double.Parse(duration.GetString() ?? "0", CultureInfo.InvariantCulture) : 0;
    }
    public async Task ExtractAudioAsync(string input, string output, double start, double? duration, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-ss", N(start), "-i", input };
        if (duration is > 0) args.AddRange(["-t", N(duration.Value)]);
        args.AddRange(["-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", output]);
        Processes.EnsureSuccess(await Processes.RunAsync(Ffmpeg, args, ct: ct), "提取音频");
    }
    public async Task<string> CreateProxyAsync(string input, string workDirectory, CancellationToken ct)
    {
        var proxy = Path.Combine(workDirectory, "playback.mp4");
        if (File.Exists(proxy)) return proxy;
        var pending = Path.Combine(workDirectory, "playback.pending.mp4");
        Processes.EnsureSuccess(await Processes.RunAsync(Ffmpeg, ["-hide_banner", "-nostdin", "-y", "-i", input, "-vf", "scale=-2:720", "-c:v", "libx264", "-preset", "veryfast", "-crf", "24", "-c:a", "aac", "-movflags", "+faststart", pending], ct: ct), "生成播放代理");
        File.Move(pending, proxy, true); return proxy;
    }
    public async Task<float[]> WaveformAsync(string input, string workDirectory, CancellationToken ct)
    {
        var cache = Path.Combine(workDirectory, "waveform.json");
        if (File.Exists(cache)) return Json.Read<float[]>(await File.ReadAllTextAsync(cache, ct));
        var raw = Path.Combine(workDirectory, "waveform.f32");
        Processes.EnsureSuccess(await Processes.RunAsync(Ffmpeg, ["-hide_banner", "-nostdin", "-y", "-i", input, "-vn", "-ac", "1", "-ar", "100", "-f", "f32le", raw], ct: ct), "生成波形");
        var peaks = new List<float>();
        using (var stream = File.OpenRead(raw))
        using (var reader = new BinaryReader(stream))
        {
            var bucket = Math.Max(1, (int)(stream.Length / 4 / 1500));
            var count = 0; var max = 0f;
            while (stream.Position + 4 <= stream.Length)
            {
                max = Math.Max(max, Math.Abs(reader.ReadSingle()));
                if (++count >= bucket) { peaks.Add(max); count = 0; max = 0; }
            }
            if (count > 0) peaks.Add(max);
        }
        var scale = peaks.Count > 0 ? peaks.Max() : 0;
        var result = peaks.Select(p => scale > 0 ? p / scale : 0).ToArray();
        await File.WriteAllTextAsync(cache, Json.Write(result), ct); File.Delete(raw); return result;
    }
    public async Task ExportVideoAsync(MediaSession session, string output, string? dubPath, bool burnSubtitles, CancellationToken ct, Action<string>? progress = null)
    {
        var work = paths.SessionDirectory(session.Id);
        var subtitleFile = Path.Combine(work, "export.ass");
        await File.WriteAllTextAsync(subtitleFile, Subtitles.Export(session, "ass"), Encoding.UTF8, ct);
        var extension = Path.GetExtension(output);
        var pending = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + ".pending" + extension);
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", session.MediaPath };
        if (dubPath is not null) args.AddRange(["-i", dubPath, "-map", "0:v:0", "-map", "1:a:0"]);
        if (burnSubtitles) args.AddRange(["-vf", "ass=export.ass", "-c:v", "libx264", "-preset", "fast", "-crf", "20"]);
        else args.AddRange(["-c:v", "copy"]);
        args.AddRange(["-c:a", "aac", "-movflags", "+faststart", "-progress", "pipe:1", pending]);
        Processes.EnsureSuccess(await Processes.RunAsync(Ffmpeg, args, work, progress, ct: ct), "视频导出");
        File.Move(pending, output, true);
    }
    public async Task ExportClipAsync(string input, string output, double start, double end, CancellationToken ct)
    {
        if (end <= start) throw new ArgumentException("片段结束时间必须大于开始时间。");
        Processes.EnsureSuccess(await Processes.RunAsync(Ffmpeg, ["-hide_banner", "-nostdin", "-y", "-ss", N(start), "-i", input, "-t", N(end - start), "-c:v", "libx264", "-preset", "fast", "-c:a", "aac", output], ct: ct), "片段导出");
    }
    public async Task<string> DownloadAsync(string url, string workDirectory, CancellationToken ct, Action<string>? progress)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("请输入有效的 HTTP/HTTPS 媒体链接。");
        Directory.CreateDirectory(workDirectory);
        var result = await Processes.RunAsync(paths.FindEngine("yt-dlp", "yt-dlp.exe"),
            ["--no-playlist", "--no-overwrites", "--newline", "--ffmpeg-location", Path.GetDirectoryName(Ffmpeg)!, "--merge-output-format", "mp4", "--print", "after_move:filepath", "-o", Path.Combine(workDirectory, "%(title).150B.%(ext)s"), "--", url], onOutput: progress, ct: ct);
        Processes.EnsureSuccess(result, "下载在线媒体");
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).LastOrDefault(File.Exists)
            ?? throw new IOException("下载结束，但没有找到媒体文件。");
    }
    public static string CacheKey(string media, string model, string options)
    {
        var m = new FileInfo(media); var w = new FileInfo(model);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{m.FullName}|{m.Length}|{m.LastWriteTimeUtc.Ticks}|{w.FullName}|{w.Length}|{w.LastWriteTimeUtc.Ticks}|{options}")))[..20].ToLowerInvariant();
    }
    internal static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
