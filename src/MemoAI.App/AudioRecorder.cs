using MemoAI.Core;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace MemoAI.App;

internal sealed class AudioRecorder(string directory, int mode, AppPaths paths) : IDisposable
{
    private readonly List<(WasapiCapture Capture, WaveFileWriter Writer, TaskCompletionSource Stopped)> devices = [];
    private readonly string id = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    private Exception? failure;
    public void Start()
    {
        try
        {
            if (mode is 0 or 2) Add(new WasapiLoopbackCapture(), "system");
            if (mode is 1 or 2) Add(new WasapiCapture(), "mic");
            foreach (var (capture, _, _) in devices) capture.StartRecording();
        }
        catch { Dispose(); throw; }
    }
    private void Add(WasapiCapture capture, string suffix)
    {
        var writer = new WaveFileWriter(Path.Combine(directory, $"{id}-{suffix}.wav"), capture.WaveFormat);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (_, args) => { lock (writer) writer.Write(args.Buffer, 0, args.BytesRecorded); };
        capture.RecordingStopped += (_, args) => { failure ??= args.Exception; stopped.TrySetResult(); };
        devices.Add((capture, writer, stopped));
    }
    public async Task<string> StopAsync()
    {
        foreach (var (capture, _, _) in devices) capture.StopRecording();
        await Task.WhenAll(devices.Select(d => d.Stopped.Task)).WaitAsync(TimeSpan.FromSeconds(10));
        foreach (var (_, writer, _) in devices) writer.Dispose();
        if (failure is not null) throw new IOException("录音失败，请检查音频设备和麦克风权限。", failure);
        var output = Path.Combine(directory, id + ".wav");
        var input = new List<string>();
        if (mode is 0 or 2) input.AddRange(["-i", Path.Combine(directory, id + "-system.wav")]);
        if (mode is 1 or 2) input.AddRange(["-i", Path.Combine(directory, id + "-mic.wav")]);
        var args = new List<string> { "-hide_banner", "-nostdin", "-y" }; args.AddRange(input);
        if (mode == 2) args.AddRange(["-filter_complex", "[0:a][1:a]amix=inputs=2:duration=longest:normalize=1"]);
        args.AddRange(["-ac", "1", "-ar", "48000", "-c:a", "pcm_s16le", output]);
        Processes.EnsureSuccess(await Processes.RunAsync(new MediaTools(paths).Ffmpeg, args), "保存录音"); return output;
    }
    public void Dispose()
    {
        foreach (var (capture, writer, _) in devices) { capture.Dispose(); writer.Dispose(); } devices.Clear();
    }
}
