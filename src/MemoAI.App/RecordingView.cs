using System.Diagnostics;
using System.Threading.Channels;
using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private sealed record RecordingAppChoice(string Name, uint ProcessId, long Started);
    private CancellationTokenSource? liveRecordingStop, recordingClipStop;
    private Channel<LiveAudioClip>? liveRecordingClips;
    private Task? liveRecordingTask, recordingClipTask;
    private MediaSession? recordingSession;
    private Task<string>? finishingRecording;
    private double recordingClipOffset;
    private int recordingPending;
    private void RecordingView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (RecordingContent is not null) RecordingContent.Width = Math.Max(0, Math.Min(940, e.NewSize.Width - 60));
    }
    private void RefreshRecordingChoices()
    {
        if (recorder is not null || liveRecordingTask is { IsCompleted: false }) return;
        var models = AsrModels.All.Where(m => m.Id != "funasr-nano-fp16" && AsrModels.IsInstalled(m, paths.ModelRoot)).Select(m => new ModelChoice(m.Name + " · ONNX", m.ManifestPath(paths.ModelRoot)))
            .Concat(ModelCatalog.Items.Where(m => m.Id.StartsWith("whisper-") && File.Exists(Path.Combine(paths.ModelRoot, m.FileName))).Select(m => new ModelChoice(m.Name, Path.Combine(paths.ModelRoot, m.FileName)))).ToList();
        if (File.Exists(settings.TranscriptionModelPath) && !models.Any(m => m.Path == settings.TranscriptionModelPath)) models.Add(new(TranscriptionEngines.ModelName(settings.TranscriptionModelPath), settings.TranscriptionModelPath));
        var selectedPath = (RecordingModel.SelectedItem as ModelChoice)?.Path ?? settings.TranscriptionModelPath;
        RecordingModel.ItemsSource = models; RecordingModel.SelectedItem = models.FirstOrDefault(m => m.Path == selectedPath) ?? models.FirstOrDefault();
        var language = (RecordingLanguage.SelectedItem as LanguageChoice)?.Code ?? settings.Language;
        RecordingLanguage.ItemsSource = languages; RecordingLanguage.SelectedItem = languages.FirstOrDefault(l => l.Code == language) ?? languages[0];
        if (RecordingMode.SelectedIndex == 3) RefreshRecordingApps();
    }
    private void RecordingMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RecordingAppPanel is null) return;
        RecordingAppPanel.Visibility = RecordingMode.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (RecordingMode.SelectedIndex == 3) RefreshRecordingApps();
    }
    private void RefreshRecordingApps_Click(object sender, RoutedEventArgs e) => RefreshRecordingApps();
    private void RefreshRecordingApps()
    {
        var selectedId = (RecordingApp.SelectedItem as RecordingAppChoice)?.ProcessId;
        var apps = new List<RecordingAppChoice>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { if (process.MainWindowHandle != IntPtr.Zero && process.MainWindowTitle.Length > 0) apps.Add(new($"{process.ProcessName} · {process.MainWindowTitle}", (uint)process.Id, process.StartTime.Ticks)); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        RecordingApp.ItemsSource = apps.OrderBy(a => a.Name).ToArray(); RecordingApp.SelectedItem = apps.FirstOrDefault(a => a.ProcessId == selectedId);
    }
    private void SetRecordingOptionsEnabled(bool enabled)
    {
        RecordingMode.IsEnabled = RecordingApp.IsEnabled = RecordingModel.IsEnabled = RecordingLanguage.IsEnabled = RecordingLive.IsEnabled = enabled;
    }
    private Task RecordingUiAsync(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() => { try { action(); done.TrySetResult(); } catch (Exception ex) { done.TrySetException(ex); } })) done.TrySetCanceled();
        return done.Task;
    }
    private void BeginLiveRecording(AppSettings options, AudioRecorder capture)
    {
        liveRecordingStop = new(); recordingClipStop = new();
        liveRecordingClips = Channel.CreateUnbounded<LiveAudioClip>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        recordingClipOffset = 0; recordingPending = 0;
        var session = recordingSession!; var clips = liveRecordingClips; var cancellation = liveRecordingStop; var clipCancellation = recordingClipStop;
        var request = new JobRequest(Guid.NewGuid().ToString("N"), session.Id, "transcribe-live", "", paths.SessionDirectory(session.Id), options);
        LiveRecordingPanel.Visibility = Visibility.Visible; CancelLiveRecording.IsEnabled = true; LiveRecordingProgress.Visibility = Visibility.Visible;
        LiveRecordingStatus.Text = "等待本地计算资源…"; LiveRecordingText.Text = "";
        liveRecordingTask = Task.Run(async () =>
        {
            var held = false;
            try
            {
                await AiService.GpuGate.WaitAsync(cancellation.Token); held = true;
                await new WorkerClient(paths).RunLiveAsync(request, clips.Reader, e => RecordingUiAsync(() =>
                {
                    if (e.Type == "segments" && e.Segments is { } segments)
                    {
                        foreach (var segment in segments) session.Segments.Add(segment);
                        store.SaveSession(session);
                        LiveRecordingText.Text = string.Join("\n", session.Segments.TakeLast(80).Select(s => s.Text));
                        LiveRecordingText.SelectionStart = LiveRecordingText.Text.Length;
                    }
                    if (e.Type == "clip-completed")
                    {
                        recordingPending = Math.Max(0, recordingPending - 1);
                        LiveRecordingStatus.Text = recordingPending == 0 ? "已跟上录音，等待下一段声音…" : $"文字已更新 · 待处理 {recordingPending} 段";
                    }
                    else LiveRecordingStatus.Text = e.Message + $" · 待处理 {recordingPending} 段";
                }), cancellation.Token);
            }
            catch (OperationCanceledException) { await RecordingUiAsync(() => LiveRecordingStatus.Text = "转写已停止，录音和已完成文字保留。"); }
            catch (Exception ex) { clipCancellation.Cancel(); await RecordingUiAsync(() => LiveRecordingStatus.Text = "转写失败，录音继续：" + ex.Message); }
            finally
            {
                if (held) AiService.GpuGate.Release();
                await RecordingUiAsync(() => { CancelLiveRecording.IsEnabled = false; LiveRecordingProgress.Visibility = Visibility.Collapsed; });
            }
        });
        recordingClipTask = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(6), clipCancellation.Token);
                    var end = recordingWatch.Elapsed.TotalSeconds;
                    while (end - recordingClipOffset >= 6) await QueueRecordingClipAsync(capture, recordingClipOffset + 6, clipCancellation.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { cancellation.Cancel(); await RecordingUiAsync(() => LiveRecordingStatus.Text = "无法准备转写片段，录音继续：" + ex.Message); }
        });
    }
    private async Task QueueRecordingClipAsync(AudioRecorder capture, double end, CancellationToken ct)
    {
        var directory = Path.Combine(paths.SessionDirectory(recordingSession!.Id), "recording-clips"); Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, $"{recordingClipOffset:000000.000}.wav");
        await capture.SnapshotAsync(output, recordingClipOffset, end, ct);
        await RecordingUiAsync(() => { recordingPending++; LiveRecordingStatus.Text = $"正在本地转写 · 待处理 {recordingPending} 段"; });
        await liveRecordingClips!.Writer.WriteAsync(new(output, recordingClipOffset), ct); recordingClipOffset = end;
    }
    private void CancelLiveRecording_Click(object sender, RoutedEventArgs e)
    {
        recordingClipStop?.Cancel(); liveRecordingStop?.Cancel(); CancelLiveRecording.IsEnabled = false;
    }
    private Task<string> FinishRecordingAsync() => finishingRecording ??= FinishRecordingCoreAsync();
    private async Task<string> FinishRecordingCoreAsync()
    {
        var capture = recorder!;
        recordingClipStop?.Cancel(); if (recordingClipTask is not null) await recordingClipTask;
        recordingWatch.Stop();
        var output = await capture.StopAsync(async () =>
        {
            if (liveRecordingClips is not null && liveRecordingStop?.IsCancellationRequested == false && liveRecordingTask is { IsCompleted: false })
            {
                var end = recordingWatch.Elapsed.TotalSeconds;
                try { if (end - recordingClipOffset > 0.1) await Task.Run(() => QueueRecordingClipAsync(capture, end, liveRecordingStop.Token)); }
                catch (Exception ex) { liveRecordingStop.Cancel(); LiveRecordingStatus.Text = "转写已停止，音频继续保存：" + ex.Message; }
            }
            liveRecordingClips?.Writer.TryComplete();
        });
        recordingSession!.MediaPath = output; recordingSession.Duration = recordingWatch.Elapsed.TotalSeconds;
        store.SaveSession(recordingSession);
        if (!sessions.Any(s => s.Id == recordingSession.Id)) sessions.Insert(0, recordingSession);
        RefreshLibrary();
        RecordingState.Text = liveRecordingTask is { IsCompleted: false } ? "音频已保存，正在完成剩余字幕…" : "录音已保存到媒体库";
        if (liveRecordingTask is not null) await liveRecordingTask;
        store.SaveSession(recordingSession);
        return output;
    }
}
