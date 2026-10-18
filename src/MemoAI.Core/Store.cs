using Microsoft.Data.Sqlite;

namespace MemoAI.Core;

public sealed partial class Store
{
    private readonly string connectionString;
    public Store(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataRoot, "library.db"), Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS sessions(id TEXT PRIMARY KEY,title TEXT NOT NULL,folder TEXT NOT NULL,updated TEXT NOT NULL,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS jobs(id TEXT PRIMARY KEY,updated TEXT NOT NULL,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS settings(id INTEGER PRIMARY KEY CHECK(id=1),payload TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_sessions_updated ON sessions(updated DESC);
            PRAGMA user_version=2;
            """;
        command.ExecuteNonQuery();
        InitializeTranscriptHistory(connection);
    }
    private SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    public void SaveSession(MediaSession session, bool recordVersion = true)
    {
        session.UpdatedAt = DateTimeOffset.Now;
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions VALUES($id,$title,$folder,$updated,$payload) ON CONFLICT(id) DO UPDATE SET title=excluded.title,folder=excluded.folder,updated=excluded.updated,payload=excluded.payload";
        cmd.Parameters.AddWithValue("$id", session.Id); cmd.Parameters.AddWithValue("$title", session.Title);
        cmd.Parameters.AddWithValue("$folder", session.Folder); cmd.Parameters.AddWithValue("$updated", session.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$payload", Json.Write(session)); cmd.ExecuteNonQuery();
        if (recordVersion) SaveTranscriptCheckpoint(session, "保存字幕");
    }
    public List<MediaSession> GetSessions(string search = "", string? folder = null)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT payload FROM sessions WHERE ($search='' OR instr(lower(title),lower($search))>0 OR instr(lower(payload),lower($search))>0) AND ($folder IS NULL OR folder=$folder) ORDER BY updated DESC";
        cmd.Parameters.AddWithValue("$search", search); cmd.Parameters.AddWithValue("$folder", (object?)folder ?? DBNull.Value);
        using var reader = cmd.ExecuteReader(); var result = new List<MediaSession>();
        while (reader.Read()) result.Add(Json.Read<MediaSession>(reader.GetString(0)));
        return result;
    }
    public MediaSession? GetSession(string id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT payload FROM sessions WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is string payload ? Json.Read<MediaSession>(payload) : null;
    }
    public void DeleteSession(string id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        using var transaction = c.BeginTransaction(); cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM transcript_heads WHERE session_id=$id; DELETE FROM transcript_revisions WHERE session_id=$id; DELETE FROM jobs WHERE json_extract(payload,'$.sessionId')=$id; DELETE FROM sessions WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery(); transaction.Commit();
    }
    public List<JobRecord> GetSessionJobs(string id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT payload FROM jobs WHERE json_extract(payload,'$.sessionId')=$id"; cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader(); var result = new List<JobRecord>();
        while (reader.Read()) result.Add(Json.Read<JobRecord>(reader.GetString(0)));
        return result;
    }
    public void SaveJob(JobRecord job)
    {
        job.UpdatedAt = DateTimeOffset.Now;
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO jobs VALUES($id,$updated,$payload) ON CONFLICT(id) DO UPDATE SET updated=excluded.updated,payload=excluded.payload";
        cmd.Parameters.AddWithValue("$id", job.Id); cmd.Parameters.AddWithValue("$updated", job.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$payload", Json.Write(job)); cmd.ExecuteNonQuery();
    }
    public List<JobRecord> GetJobs()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT payload FROM jobs ORDER BY updated DESC LIMIT 200";
        using var reader = cmd.ExecuteReader(); var result = new List<JobRecord>();
        while (reader.Read()) result.Add(Json.Read<JobRecord>(reader.GetString(0)));
        return result;
    }
    public AppSettings LoadSettings()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT payload FROM settings WHERE id=1";
        return cmd.ExecuteScalar() is string s ? Json.Read<AppSettings>(s) : new();
    }
    public void SaveSettings(AppSettings settings)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO settings VALUES(1,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
        cmd.Parameters.AddWithValue("$payload", Json.Write(settings)); cmd.ExecuteNonQuery();
    }
}
