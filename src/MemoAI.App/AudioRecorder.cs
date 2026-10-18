using MemoAI.Core;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace MemoAI.App;

internal sealed class AudioRecorder(string directory, int mode, AppPaths paths, uint processId = 0) : IDisposable
{
    private readonly List<(IWaveIn Capture, WaveFileWriter Writer, TaskCompletionSource Stopped, string File)> devices = [];
    private readonly string id = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "-" + Guid.NewGuid().ToString("N")[..6];
    private readonly System.Diagnostics.Stopwatch timeline = new();
    private Exception? failure;
    public string OutputPath => Path.Combine(directory, id + ".wav");
    public async Task StartAsync()
    {
        try
        {
            if (mode is 0 or 2) Add(new WasapiLoopbackCapture(), "system");
            if (mode is 1 or 2) Add(new WasapiCapture(), "mic");
            if (mode == 3)
            {
                if (processId == 0) throw new ArgumentException("请选择要内录的 App。");
                Add(new ProcessAudioCapture(processId), "app");
            }
            timeline.Start();
            foreach (var (capture, _, _, _) in devices)
            {
                await Task.Run(capture.StartRecording);
                if (capture is ProcessAudioCapture app) await app.WaitUntilReadyAsync();
            }
        }
        catch { Dispose(); throw; }
    }
    private void Add(IWaveIn capture, string suffix)
    {
        var file = Path.Combine(directory, $"{id}-{suffix}.wav");
        var writer = new WaveFileWriter(file, capture.WaveFormat);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (_, args) =>
        {
            lock (writer)
            {
                // Loopback can stop emitting packets during silence. Preserve wall-clock offsets.
                var expected = (long)(Math.Max(0, timeline.Elapsed.TotalSeconds - args.BytesRecorded / (double)capture.WaveFormat.AverageBytesPerSecond) * capture.WaveFormat.AverageBytesPerSecond);
                expected -= expected % capture.WaveFormat.BlockAlign;
                var gap = expected - writer.Length;
                if (gap > capture.WaveFormat.AverageBytesPerSecond / 10)
                {
                    var silence = new byte[65536]; while (gap > 0) { var n = (int)Math.Min(gap, silence.Length); writer.Write(silence, 0, n); gap -= n; }
                }
                writer.Write(args.Buffer, 0, args.BytesRecorded);
            }
        };
        capture.RecordingStopped += (_, args) => { failure ??= args.Exception; stopped.TrySetResult(); };
        devices.Add((capture, writer, stopped, file));
    }
    public async Task<string> SnapshotAsync(string output, double start, double end, CancellationToken ct)
    {
        if (end <= start) throw new ArgumentException("录音片段时间无效。");
        var clips = new List<string>();
        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i]; var file = output + $".source-{i}.wav"; clips.Add(file);
            lock (device.Writer)
            {
                device.Writer.Flush();
                using var source = new WaveFileReader(new FileStream(device.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                using var target = new WaveFileWriter(file, device.Capture.WaveFormat);
                var format = device.Capture.WaveFormat;
                var from = (long)(start * format.AverageBytesPerSecond); from -= from % format.BlockAlign;
                var bytes = (long)((end - start) * format.AverageBytesPerSecond); bytes -= bytes % format.BlockAlign;
                source.Position = Math.Min(from, source.Length); var available = Math.Min(bytes, Math.Max(0, source.Length - from)); var buffer = new byte[65536];
                while (available > 0) { var n = source.Read(buffer, 0, (int)Math.Min(buffer.Length, available)); if (n == 0) break; target.Write(buffer, 0, n); available -= n; }
                Array.Clear(buffer); var padding = bytes - target.Length; while (padding > 0) { var n = (int)Math.Min(buffer.Length, padding); target.Write(buffer, 0, n); padding -= n; }
            }
        }
        var args = new List<string> { "-hide_banner", "-nostdin", "-y" }; foreach (var clip in clips) args.AddRange(["-i", clip]);
        if (clips.Count > 1) args.AddRange(["-filter_complex", "[0:a][1:a]amix=inputs=2:duration=longest:normalize=1"]);
        args.AddRange(["-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", output]);
        Processes.EnsureSuccess(await Processes.RunAsync(new MediaTools(paths).Ffmpeg, args, ct: ct), "准备录音转写片段");
        foreach (var clip in clips) File.Delete(clip);
        return output;
    }
    public async Task<string> StopAsync(Func<Task>? beforeSave = null)
    {
        foreach (var (capture, _, _, _) in devices) capture.StopRecording();
        await Task.WhenAll(devices.Select(d => d.Stopped.Task)).WaitAsync(TimeSpan.FromSeconds(10));
        timeline.Stop();
        foreach (var (capture, writer, _, _) in devices)
        {
            var expected = (long)(timeline.Elapsed.TotalSeconds * capture.WaveFormat.AverageBytesPerSecond); expected -= expected % capture.WaveFormat.BlockAlign;
            var remaining = expected - writer.Length; var silence = new byte[65536];
            while (remaining > 0) { var n = (int)Math.Min(silence.Length, remaining); writer.Write(silence, 0, n); remaining -= n; }
        }
        if (beforeSave is not null) await beforeSave();
        foreach (var (_, writer, _, _) in devices) writer.Dispose();
        if (failure is not null) throw new IOException("录音失败，请检查音频设备和麦克风权限。", failure);
        var output = Path.Combine(directory, id + ".wav");
        var input = new List<string>();
        if (mode is 0 or 2) input.AddRange(["-i", Path.Combine(directory, id + "-system.wav")]);
        if (mode is 1 or 2) input.AddRange(["-i", Path.Combine(directory, id + "-mic.wav")]);
        if (mode == 3) input.AddRange(["-i", Path.Combine(directory, id + "-app.wav")]);
        var args = new List<string> { "-hide_banner", "-nostdin", "-y" }; args.AddRange(input);
        if (mode == 2) args.AddRange(["-filter_complex", "[0:a][1:a]amix=inputs=2:duration=longest:normalize=1"]);
        args.AddRange(["-ac", "1", "-ar", "48000", "-c:a", "pcm_s16le", output]);
        Processes.EnsureSuccess(await Processes.RunAsync(new MediaTools(paths).Ffmpeg, args), "保存录音"); return output;
    }
    public void Dispose()
    {
        foreach (var (capture, writer, _, _) in devices) { capture.Dispose(); writer.Dispose(); } devices.Clear();
    }
}
