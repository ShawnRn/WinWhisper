using System.Threading.Channels;

namespace MemoAI.Core;

public sealed class JobScheduler : IJobScheduler, IAsyncDisposable
{
    private readonly AppPaths paths;
    private readonly Store store;
    private readonly Func<AppSettings> settings;
    private readonly Func<JobRecord, JobEvent?, Task> notify;
    private readonly Channel<JobRecord> queue = Channel.CreateUnbounded<JobRecord>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<string, CancellationTokenSource> cancellations = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task consumer;
    public JobScheduler(AppPaths paths, Store store, Func<AppSettings> settings, Func<JobRecord, JobEvent?, Task> notify)
    {
        this.paths = paths; this.store = store; this.settings = settings; this.notify = notify;
        foreach (var job in store.GetJobs().Where(j => j.State is JobState.Running or JobState.Queued))
        { job.State = JobState.Interrupted; job.Message = "应用上次退出时任务未完成，可重试并使用已有检查点"; store.SaveJob(job); }
        consumer = Task.Run(ConsumeAsync);
    }
    public async Task<JobRecord> EnqueueAsync(MediaSession session, string operation, CancellationToken ct = default)
    {
        var job = new JobRecord { SessionId = session.Id, Title = session.Title, Operation = operation };
        var defaults = settings();
        var snapshot = operation == "transcribe" && session.Transcription is not null ? session.Transcription.ApplyTo(defaults) : Json.Read<AppSettings>(Json.Write(defaults));
        snapshot.EngineRoot = paths.EngineRoot;
        job.Request = new(job.Id, session.Id, operation, session.MediaPath, paths.SessionDirectory(session.Id), snapshot, InputSegments: session.Segments.Select(s => s.Copy()).ToArray());
        await AddAsync(job, ct); return job;
    }
    public async Task EnqueueRangeAsync(MediaSession session, double start, double end, CancellationToken ct = default)
    {
        var job = new JobRecord { SessionId = session.Id, Title = session.Title, Operation = "transcribe-range" };
        var snapshot = session.Transcription?.ApplyTo(settings()) ?? Json.Read<AppSettings>(Json.Write(settings())); snapshot.EngineRoot = paths.EngineRoot;
        job.Request = new(job.Id, session.Id, "transcribe", session.MediaPath, paths.SessionDirectory(session.Id), snapshot, start, end);
        await AddAsync(job, ct);
    }
    public async Task RetryAsync(JobRecord original, CancellationToken ct = default)
    {
        if (original.Request is null) throw new InvalidOperationException("该任务缺少可恢复的请求。");
        var job = new JobRecord { SessionId = original.SessionId, Title = original.Title, Operation = original.Operation };
        // Retry uses current settings so a missing model or backend can be corrected.
        var options = store.GetSession(original.SessionId)?.Transcription;
        var snapshot = original.Request.Operation == "transcribe" && options is not null ? options.ApplyTo(settings()) : Json.Read<AppSettings>(Json.Write(settings())); snapshot.EngineRoot = paths.EngineRoot;
        job.Request = original.Request with { Id = job.Id, Settings = snapshot };
        await AddAsync(job, ct);
    }
    private async Task AddAsync(JobRecord job, CancellationToken ct)
    {
        lock (cancellations) cancellations[job.Id] = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        store.SaveJob(job); await notify(job, null); await queue.Writer.WriteAsync(job, ct);
    }
    public void Cancel(string jobId) { lock (cancellations) if (cancellations.TryGetValue(jobId, out var source)) source.Cancel(); }
    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(lifetime.Token))
            {
                CancellationTokenSource source; lock (cancellations) source = cancellations[job.Id];
                var gateHeld = false;
                try
                {
                    await AiService.GpuGate.WaitAsync(source.Token); gateHeld = true;
                    job.State = JobState.Running; job.StartedAt = DateTimeOffset.Now; job.Message = "正在初始化"; store.SaveJob(job); await notify(job, null);
                    await new WorkerClient(paths).RunAsync(job.Request!, async e =>
                    {
                        job.Message = e.Message; job.Progress = e.Progress;
                        if (e.Backend is not null) job.Backend = e.Backend;
                        if (e.Device is not null) job.Device = e.Device;
                        job.ElapsedSeconds = e.ElapsedSeconds ?? job.ElapsedSeconds; job.RealTimeFactor = e.RealTimeFactor ?? job.RealTimeFactor;
                        store.SaveJob(job); await notify(job, e);
                    }, source.Token);
                    job.State = JobState.Completed; job.Progress = 1;
                }
                catch (OperationCanceledException) { job.State = lifetime.IsCancellationRequested ? JobState.Interrupted : JobState.Cancelled; job.Message = "任务已停止，已完成片段保留，可重试"; }
                catch (Exception ex) { job.State = JobState.Failed; job.Message = ex.Message; }
                finally
                {
                    if (gateHeld) AiService.GpuGate.Release();
                    lock (cancellations) { cancellations.Remove(job.Id); source.Dispose(); }
                    store.SaveJob(job); await notify(job, null);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); queue.Writer.TryComplete();
        lock (cancellations) foreach (var source in cancellations.Values) source.Cancel();
        await consumer; lifetime.Dispose();
    }
}
