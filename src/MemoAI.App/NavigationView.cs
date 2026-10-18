using System.Diagnostics;
using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private bool navigationReady, synchronizingNavigation;
    private readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer recordingTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch recordingWatch = new();
    private CancellationTokenSource? linkCancellation;

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!navigationReady || synchronizingNavigation) return;
        switch ((args.SelectedItem as NavigationViewItem)?.Tag as string)
        {
            case "library": Library_Click(sender, new()); break;
            case "queue": Queue_Click(sender, new()); break;
            case "record": ShowPage(RecordView); break;
            case "link": ShowPage(LinkView); break;
            case "models": ModelStore_Click(sender, new()); break;
            case "settings": Settings_Click(sender, new()); break;
        }
    }

    private async void PasteLink_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        ShowPage(LinkView);
        if (linkCancellation is not null) return;
        var clipboard = Clipboard.GetContent();
        if (clipboard.Contains(StandardDataFormats.Text)) LinkInput.Text = (await clipboard.GetTextAsync()).Trim();
        LinkInput.Focus(FocusState.Programmatic);
    });
    private void CancelLink_Click(object sender, RoutedEventArgs e) { if (linkCancellation is not null) { linkCancellation.Cancel(); LinkCancel.IsEnabled = false; LinkDownloadStatus.Text = "正在取消下载…"; } }
    private async void DownloadLink_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        if (linkCancellation is not null) return;
        if (!Uri.TryCreate(LinkInput.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ArgumentException("请输入有效的 http 或 https 媒体链接。");
        var autoTranscribe = LinkAutoTranscribe.IsChecked == true;
        var transcription = TranscriptionOptions.FromSettings(settings);
        if (autoTranscribe && !File.Exists(transcription.ModelPath)) throw new FileNotFoundException("请先在模型商店下载并应用 Whisper 模型，或取消「自动开始本地转写」。");
        var options = new OnlineDownloadOptions((LinkCookieBrowser.SelectedItem as ComboBoxItem)?.Tag as string ?? "", LinkBrowserProfile.Text.Trim(), LinkCookieFile.Text, LinkProxy.Text.Trim(), LinkQuality.SelectedIndex);
        using var cancellation = new CancellationTokenSource(); linkCancellation = cancellation;
        LinkDownload.IsEnabled = LinkInput.IsEnabled = false; LinkCancel.Visibility = LinkProgress.Visibility = Visibility.Visible;
        LinkCancel.IsEnabled = true; LinkDownload.Content = "下载中…";
        LinkDownloadStatus.Text = "正在连接媒体来源…";
        try
        {
            var session = new MediaSession { SourceUrl = uri.AbsoluteUri, Title = "在线媒体", Transcription = autoTranscribe ? transcription : null };
            session.MediaPath = await new MediaTools(paths).DownloadAsync(session.SourceUrl, paths.SessionDirectory(session.Id), cancellation.Token,
                line => DispatcherQueue.TryEnqueue(() => { if (linkCancellation == cancellation) LinkDownloadStatus.Text = line; }), options);
            session.Title = Path.GetFileNameWithoutExtension(session.MediaPath);
            session.Duration = await new MediaTools(paths).ProbeAsync(session.MediaPath, cancellation.Token);
            store.SaveSession(session); sessions.Insert(0, session); RefreshLibrary();
            if (autoTranscribe) await scheduler.EnqueueAsync(session, "transcribe", cancellation.Token);
            LinkDownloadStatus.Text = "已导入：" + session.Title;
            if (LinkView.Visibility == Visibility.Visible) await OpenSessionAsync(session);
            if (autoTranscribe) RefreshTranscriptionProgress();
            else Status("在线媒体已导入");
        }
        catch (OperationCanceledException) { LinkDownloadStatus.Text = "下载已取消，可以修改链接后重试。"; }
        catch (Exception ex) { LinkDownloadStatus.Text = "下载失败：" + ex.Message; throw; }
        finally { linkCancellation = null; LinkDownload.IsEnabled = LinkInput.IsEnabled = true; LinkDownload.Content = "下载并导入"; LinkCancel.Visibility = LinkProgress.Visibility = Visibility.Collapsed; }
    });
    private async void ChooseCookieFile_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () => { var files = await PickFilesAsync(".txt"); if (files.Count > 0) LinkCookieFile.Text = files[0]; });
    private void ClearCookieFile_Click(object sender, RoutedEventArgs e) => LinkCookieFile.Text = "";

    private async void Record_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RecordingAction.IsEnabled = false;
        try
        {
            if (recorder is not null)
            {
                RecordingState.Text = "正在保存录音…";
                string output;
                try { output = await recorder.StopAsync(); }
                finally { recorder.Dispose(); recorder = null; recordingTimer.Stop(); recordingWatch.Stop(); RecordingMode.IsEnabled = true; RecordingBadge.Visibility = Visibility.Collapsed; RecordingAction.Content = "开始录音"; }
                RecordingState.Text = "录音已保存";
                await ImportPathsAsync([output]); Status("录音已保存到媒体库");
            }
            else
            {
                var directory = Path.Combine(paths.DataRoot, "recordings"); Directory.CreateDirectory(directory);
                var capture = new AudioRecorder(directory, RecordingMode.SelectedIndex, paths); capture.Start(); recorder = capture;
                recordingWatch.Restart(); recordingTimer.Start(); RecordingTime.Text = "00:00:00";
                RecordingMode.IsEnabled = false; RecordingBadge.Visibility = Visibility.Visible;
                RecordingAction.Content = "停止并保存"; RecordingState.Text = "正在录音 · 可以切换功能区，录音会继续。";
            }
        }
        catch (Exception ex) { RecordingState.Text = "录音未完成：" + ex.Message; throw; }
        finally { RecordingAction.IsEnabled = true; }
    });
}
