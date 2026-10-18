using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MemoAI.Core;

public sealed class TranscriptRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public string? ParentId { get; set; }
    public string BaseHash { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string Instruction { get; set; } = "";
    public string Kind { get; set; } = "Edit";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public SubtitleSegment[] Before { get; set; } = [];
    public SubtitleSegment[] After { get; set; } = [];
    public List<TranscriptAgentEvent> Events { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public bool IsCurrent { get; set; }
    public string Label => $"{(IsCurrent ? "当前 · " : "")}{Id[..Math.Min(8, Id.Length)]} · {CreatedAt:MM-dd HH:mm:ss} · {Status switch { "Applied" => "已应用", "Rejected" => "已拒绝", "Generating" => "生成中 / 上次中断", "Cancelled" => "已停止", "Failed" => "失败", "NoChanges" => "无需修改", _ => "待审阅" }} · {Instruction}";
    public static string Hash(IEnumerable<SubtitleSegment> cues) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json.Write(cues))));
}

public sealed record TranscriptDiff(string TimeLabel, string Kind, string Before, string After, string Context = "")
{
    public static List<TranscriptDiff> Build(IReadOnlyList<SubtitleSegment> before, IReadOnlyList<SubtitleSegment> after)
    {
        static string Time(SubtitleSegment cue) => Subtitles.FormatTime(cue.Start, true) + " → " + Subtitles.FormatTime(cue.End, true);
        var old = before.ToDictionary(c => c.Id); var fresh = after.ToDictionary(c => c.Id); var rows = new List<(double, TranscriptDiff)>();
        foreach (var cue in before)
        {
            if (!fresh.TryGetValue(cue.Id, out var next)) rows.Add((cue.Start, new(Time(cue), "删除 / 重新切分", cue.Text + (cue.Translation.Length > 0 ? "\n译文：" + cue.Translation : ""), "")));
            else if (cue.Text != next.Text || cue.Translation != next.Translation || cue.Start != next.Start || cue.End != next.End || cue.Speaker != next.Speaker)
            {
                var time = Time(cue) + (Time(cue) == Time(next) ? "" : " ⇒ " + Time(next));
                var speaker = cue.Speaker == next.Speaker ? "" : $" · 说话人：{cue.Speaker} → {next.Speaker}";
                if (cue.Text == next.Text)
                {
                    var translationChanged = cue.Translation != next.Translation;
                    var kind = translationChanged ? cue.Translation.Length == 0 ? "新增译文" : next.Translation.Length == 0 ? "删除译文" : "修改译文" : "时间 / 说话人修改";
                    rows.Add((cue.Start, new(time, kind + speaker, translationChanged ? cue.Translation : "", translationChanged ? next.Translation : "", cue.Text + (!translationChanged && cue.Translation.Length > 0 ? "\n译文：" + cue.Translation : ""))));
                }
                else rows.Add((cue.Start, new(time, "修改" + speaker, cue.Text + (cue.Translation.Length > 0 ? "\n译文：" + cue.Translation : ""), next.Text + (next.Translation.Length > 0 ? "\n译文：" + next.Translation : ""))));
            }
        }
        foreach (var cue in after.Where(c => !old.ContainsKey(c.Id))) rows.Add((cue.Start, new(Time(cue), "新增 / 重新切分", "", cue.Text + (cue.Translation.Length > 0 ? "\n译文：" + cue.Translation : ""))));
        return rows.OrderBy(r => r.Item1).Select(r => r.Item2).ToList();
    }
}

public sealed partial class Store
{
    private static void InitializeTranscriptHistory(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS transcript_revisions(id TEXT PRIMARY KEY,session_id TEXT NOT NULL,created TEXT NOT NULL,status TEXT NOT NULL,payload TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_transcript_history ON transcript_revisions(session_id,status,created DESC); CREATE TABLE IF NOT EXISTS transcript_heads(session_id TEXT PRIMARY KEY,revision_id TEXT NOT NULL); INSERT OR IGNORE INTO transcript_heads SELECT r.session_id,r.id FROM transcript_revisions r WHERE r.status='Applied' AND r.rowid=(SELECT MAX(v.rowid) FROM transcript_revisions v WHERE v.session_id=r.session_id AND v.status='Applied');";
        cmd.ExecuteNonQuery();
    }
    public List<TranscriptRevision> GetTranscriptVersions(string sessionId, bool metadataOnly = false)
    {
        using var c = Open(); var versions = ReadVersions(c, null, sessionId, metadataOnly); var head = LatestVersion(c, null, sessionId)?.Id;
        foreach (var version in versions) version.IsCurrent = version.Id == head; return versions;
    }
    public TranscriptRevision GetTranscriptVersion(string sessionId, string id)
    { using var c = Open(); return ReadVersion(c, null, sessionId, id); }
    private static TranscriptRevision ReadVersion(SqliteConnection c, SqliteTransaction? tx, string sessionId, string id)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT payload FROM transcript_revisions WHERE id=$id AND session_id=$s";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$s", sessionId);
        return cmd.ExecuteScalar() is string json ? Json.Read<TranscriptRevision>(json) : throw new InvalidOperationException("此媒体没有对应提案。");
    }
    private static TranscriptRevision? LatestVersion(SqliteConnection c, SqliteTransaction? tx, string sessionId)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT v.payload FROM transcript_heads h JOIN transcript_revisions v ON v.id=h.revision_id WHERE h.session_id=$s AND v.status='Applied'"; cmd.Parameters.AddWithValue("$s", sessionId);
        return cmd.ExecuteScalar() is string json ? Json.Read<TranscriptRevision>(json) : null;
    }
    public string? GetTranscriptHead(string sessionId) { using var c = Open(); return LatestVersion(c, null, sessionId)?.Id; }
    private static void SetTranscriptHead(SqliteConnection c, SqliteTransaction tx, TranscriptRevision revision)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO transcript_heads VALUES($s,$id) ON CONFLICT(session_id) DO UPDATE SET revision_id=excluded.revision_id";
        cmd.Parameters.AddWithValue("$s", revision.SessionId); cmd.Parameters.AddWithValue("$id", revision.Id); cmd.ExecuteNonQuery();
    }
    private static List<TranscriptRevision> ReadVersions(SqliteConnection c, SqliteTransaction? tx, string sessionId, bool metadataOnly = false)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT " + (metadataOnly ? "json_remove(payload,'$.before','$.after','$.events')" : "payload") + " FROM transcript_revisions WHERE session_id=$s ORDER BY created DESC,rowid DESC"; cmd.Parameters.AddWithValue("$s", sessionId);
        using var reader = cmd.ExecuteReader(); var list = new List<TranscriptRevision>();
        while (reader.Read()) list.Add(Json.Read<TranscriptRevision>(reader.GetString(0))); return list;
    }
    private static void WriteVersion(SqliteConnection c, SqliteTransaction? tx, TranscriptRevision revision)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO transcript_revisions VALUES($id,$s,$created,$status,$p) ON CONFLICT(id) DO UPDATE SET status=excluded.status,payload=excluded.payload";
        cmd.Parameters.AddWithValue("$status", revision.Status);
        cmd.Parameters.AddWithValue("$id", revision.Id); cmd.Parameters.AddWithValue("$s", revision.SessionId); cmd.Parameters.AddWithValue("$created", revision.CreatedAt.ToString("O")); cmd.Parameters.AddWithValue("$p", Json.Write(revision)); cmd.ExecuteNonQuery();
    }
    public void SaveTranscriptCheckpoint(MediaSession session, string description)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        var parent = LatestVersion(c, tx, session.Id);
        if (parent is not null && TranscriptRevision.Hash(parent.After) == TranscriptRevision.Hash(session.Segments) || parent is null && session.Segments.Count == 0) return;
        var checkpoint = new TranscriptRevision { SessionId = session.Id, ParentId = parent?.Id, BaseHash = TranscriptRevision.Hash(parent?.After ?? []), Before = parent?.After ?? [], After = session.Segments.Select(s => s.Copy()).ToArray(), Status = "Applied", Kind = "Checkpoint", Instruction = description };
        WriteVersion(c, tx, checkpoint); SetTranscriptHead(c, tx, checkpoint); tx.Commit();
    }
    public TranscriptRevision CreateTranscriptProposal(MediaSession session, IReadOnlyList<SubtitleSegment> before, IReadOnlyList<SubtitleSegment> after, string instruction, IEnumerable<TranscriptAgentEvent>? events = null, string status = "Pending", string kind = "Edit")
    {
        if (status is not ("Pending" or "Generating")) throw new ArgumentException("新提案必须等待生成或审阅。");
        if (TranscriptRevision.Hash(session.Segments) != TranscriptRevision.Hash(before)) throw new InvalidOperationException("转写已变化，未创建过期提案。");
        SaveSession(session);
        using var c = Open(); using var tx = c.BeginTransaction();
        var parent = LatestVersion(c, tx, session.Id);
        var revision = new TranscriptRevision { SessionId = session.Id, ParentId = parent?.Id, BaseHash = TranscriptRevision.Hash(before), Before = before.Select(s => s.Copy()).ToArray(), After = after.Select(s => s.Copy()).ToArray(), Instruction = instruction, Events = events?.ToList() ?? [], Status = status, Kind = kind };
        WriteVersion(c, tx, revision); tx.Commit(); return revision;
    }
    public void UpdateTranscriptRun(TranscriptRevision revision)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); var saved = ReadVersion(c, tx, revision.SessionId, revision.Id);
        if (saved.Status != "Generating") throw new InvalidOperationException("已完成的提案不能被生成事件覆盖。");
        if (revision.Status is not ("Generating" or "Pending" or "Cancelled" or "Failed" or "NoChanges")) throw new InvalidOperationException("生成过程不能允许或拒绝提案。");
        if (revision.BaseHash != saved.BaseHash || revision.ParentId != saved.ParentId || TranscriptRevision.Hash(revision.Before) != saved.BaseHash) throw new InvalidOperationException("提案的基准版本不一致。");
        WriteVersion(c, tx, revision); tx.Commit();
    }
    public TranscriptRevision DecideTranscriptProposal(MediaSession session, string revisionId, bool allow)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        var revision = ReadVersion(c, tx, session.Id, revisionId);
        if (revision.Status != "Pending")
        { if (revision.Status == (allow ? "Applied" : "Rejected")) return revision; throw new InvalidOperationException("此提案已处理，不能重复执行不同决定。"); }
        if (allow)
        {
            using var read = c.CreateCommand(); read.Transaction = tx; read.CommandText = "SELECT payload FROM sessions WHERE id=$id"; read.Parameters.AddWithValue("$id", session.Id);
            var persisted = read.ExecuteScalar() is string json ? Json.Read<MediaSession>(json) : throw new InvalidOperationException("媒体已删除。");
            if (TranscriptRevision.Hash(session.Segments) != revision.BaseHash || TranscriptRevision.Hash(persisted.Segments) != revision.BaseHash || LatestVersion(c, tx, session.Id)?.Id != revision.ParentId) throw new InvalidOperationException("字幕在提案生成后已变化，旧提案不能覆盖新版本。可以拒绝后重新生成。");
            var copy = Json.Read<MediaSession>(Json.Write(session)); copy.Segments = new(revision.After.Select(s => s.Copy())); copy.UpdatedAt = DateTimeOffset.Now;
            using var write = c.CreateCommand(); write.Transaction = tx; write.CommandText = "UPDATE sessions SET payload=$p,updated=$u WHERE id=$id";
            write.Parameters.AddWithValue("$p", Json.Write(copy)); write.Parameters.AddWithValue("$u", copy.UpdatedAt.ToString("O")); write.Parameters.AddWithValue("$id", session.Id); write.ExecuteNonQuery();
        }
        revision.Status = allow ? "Applied" : "Rejected";
        revision.Events.Add(new(revision.Events.FirstOrDefault()?.RunId ?? revision.Id, revision.Events.Count + 1, "review", allow ? "allowed" : "rejected", allow ? "用户允许应用修改" : "用户拒绝修改，原文保留", DateTimeOffset.Now));
        WriteVersion(c, tx, revision); if (allow) SetTranscriptHead(c, tx, revision); tx.Commit(); return revision;
    }
}
