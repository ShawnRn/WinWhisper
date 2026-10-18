using System.Text.RegularExpressions;

namespace MemoAI.Core;

public static class SessionFiles
{
    // Only app-owned paths are eligible. Never follow a directory junction or infer
    // ownership from an arbitrary file extension or a similarly prefixed folder.
    public static void DeleteOwned(AppPaths paths, MediaSession session, IEnumerable<MediaSession> remaining)
    {
        if (!Guid.TryParseExact(session.Id, "N", out _)) throw new ArgumentException("无效的媒体 ID");
        var root = Path.GetFullPath(paths.DataRoot);
        var cache = Path.Combine(root, "sessions", session.Id);
        var recordingRoot = Path.Combine(root, "recordings");
        var others = remaining.ToArray();
        var cachePrefix = cache + Path.DirectorySeparatorChar;
        if (others.SelectMany(s => new[] { s.MediaPath, s.RecordingPath, s.PlaybackPath }).Any(p => !string.IsNullOrWhiteSpace(p) && Path.GetFullPath(p).StartsWith(cachePrefix, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("其他媒体仍在使用此会话中的文件，请先调整关联再删除。");
        var recording = OwnedRecording(recordingRoot, session.RecordingPath.Length > 0 ? session.RecordingPath : session.MediaPath);
        var files = new List<string>();
        if (recording is not null && !others.Any(s => SamePath(s.MediaPath, recording) || SamePath(s.RecordingPath, recording)))
        {
            var stem = Path.Combine(recordingRoot, Path.GetFileNameWithoutExtension(recording));
            files.AddRange(new[] { recording, stem + "-system.wav", stem + "-mic.wav", stem + "-app.wav" }
                .Where(f => File.Exists(f) && !others.Any(s => SamePath(s.MediaPath, f) || SamePath(s.RecordingPath, f) || SamePath(s.PlaybackPath, f))));
        }
        CheckParents(root, cache);
        if (Directory.Exists(cache)) CheckTree(cache);
        foreach (var file in files) CheckParents(root, file);
        foreach (var file in files) File.Delete(file);
        if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
    }
    private static string? OwnedRecording(string root, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = Path.GetFullPath(value);
        return SamePath(Path.GetDirectoryName(path), root) && Regex.IsMatch(Path.GetFileName(path), @"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}-[0-9a-f]{6}\.wav$", RegexOptions.CultureInvariant)
            ? path : null;
    }
    private static bool SamePath(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private static void CheckParents(string root, string target)
    {
        if (!Path.GetFullPath(target).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("清理路径不在应用数据目录内。");
        for (var cursor = target; cursor is not null; cursor = Path.GetDirectoryName(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("清理路径包含链接，已保留文件：" + cursor);
        }
    }
    private static void CheckTree(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("缓存目录包含链接，已保留文件：" + entry);
            if (attributes.HasFlag(FileAttributes.Directory)) CheckTree(entry);
        }
    }
}
