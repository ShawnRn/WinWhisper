using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MemoAI.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace MemoAI.App;

public sealed partial class MainWindow : Window
{
    private readonly AppPaths paths = new(Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--data-root="))?[12..]);
    private readonly Store store;
    private AppSettings settings;
    private readonly ObservableCollection<MediaSession> sessions;
    private readonly ObservableCollection<SubtitleSegment> visibleSegments = [];
    private readonly ObservableCollection<string> jobLabels = [];
    private List<JobRecord> jobs = [];
    private readonly JobScheduler scheduler;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly DispatcherTimer playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Stack<string> undo = [];
    private MediaSession? current;
    private SubtitleSegment? selected;
    private bool selecting, loading, dirty, closing, aiBusy;
    private CancellationTokenSource? aiCancellation;
    private LocalLanguageModelProvider? provider;
    private float[] waveform = [];
    private AudioRecorder? recorder;
    private FloatingSubtitleWindow? floating;
    private string? lastFollowId;
    private readonly SemaphoreSlim dialogs = new(1, 1);

    public MainWindow()
    {
        InitializeComponent();
        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        ExtendsContentIntoTitleBar = true; SetTitleBar(TitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 980));
        store = new Store(paths.DataRoot); settings = store.LoadSettings(); ApplySettings();
        sessions = new(store.GetSessions());
        RecentList.ItemsSource = sessions; SubtitleList.ItemsSource = visibleSegments; JobList.ItemsSource = jobLabels;
        scheduler = new JobScheduler(paths, store, () => settings, NotifyJobAsync);
        RefreshLibrary(); RefreshJobs(); BuildSettings();
        var mediaPlayer = new MediaPlayer { AutoPlay = false };
        mediaPlayer.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() => Player_MediaOpened(this, new RoutedEventArgs()));
        mediaPlayer.MediaFailed += (_, args) => DispatcherQueue.TryEnqueue(() => Player_MediaFailed(args.ErrorMessage));
        Player.SetMediaPlayer(mediaPlayer);
        ArchitectureLabel.Text = $"{AppPaths.Rid.Replace("win-", "").ToUpperInvariant()}  ·  本地 AI";
        saveTimer.Tick += async (_, _) => { saveTimer.Stop(); if (dirty) await SafeAsync(SaveCurrentAsync); };
        playbackTimer.Tick += PlaybackTick; playbackTimer.Start();
        Player.TransportControls.IsPlaybackRateButtonVisible = true;
        Player.TransportControls.IsPlaybackRateEnabled = true;
        Root.KeyboardAccelerators.Add(Accelerator(VirtualKey.O, VirtualKeyModifiers.Control, Import_Click));
        Root.KeyboardAccelerators.Add(Accelerator(VirtualKey.F, VirtualKeyModifiers.Control, (_, _) => SubtitleSearch.Focus(FocusState.Programmatic)));
        Root.KeyboardAccelerators.Add(Accelerator(VirtualKey.Z, VirtualKeyModifiers.Control, Undo_Click));
        Closed += Window_Closed;
        AppWindow.Closing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true; playbackTimer.Stop(); saveTimer.Stop(); aiCancellation?.Cancel();
            foreach (var cancellation in modelDownloads.Values) cancellation.Cancel();
            try { await SaveCurrentAsync(); await scheduler.DisposeAsync(); provider?.Dispose(); if (recorder is not null) await recorder.StopAsync(); }
            catch (Exception ex) { File.AppendAllText(Path.Combine(paths.DataRoot, "shutdown.log"), ex + "\n"); }
            finally { recorder?.Dispose(); recorder = null; floating?.Close(); Close(); }
        };
        Root.Loaded += (_, _) =>
        {
            var args = Environment.GetCommandLineArgs();
            var test = args.FirstOrDefault(a => a.StartsWith("--import="));
            if (test is not null) _ = SafeAsync(() => ImportPathsAsync([test[9..]]));
            if (args.Contains("--ui-self-test")) _ = RunUiSelfTestAsync();
        };
    }
    private KeyboardAccelerator Accelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action<object, RoutedEventArgs> action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) => { action(this, new RoutedEventArgs()); e.Handled = true; }; return accelerator;
    }
    private async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { Status("操作已取消"); }
        catch (Exception ex) { Status(ex.Message); await MessageAsync("操作未完成", ex.Message); }
    }
    private void Status(string message, double? progress = null)
    {
        StatusText.Text = message.Replace('\n', ' ');
        StatusProgress.Visibility = progress.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (progress.HasValue) StatusProgress.Value = progress.Value * 100;
    }
    private async Task MessageAsync(string title, string message)
    {
        if (Root.XamlRoot is null || closing) return;
        await dialogs.WaitAsync();
        try { await new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, Content = new ScrollViewer { MaxHeight = 400, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } }, CloseButtonText = "知道了" }.ShowAsync(); }
        finally { dialogs.Release(); }
    }
    private async Task<bool> ConfirmAsync(string title, object content, string confirm = "确定")
    {
        await dialogs.WaitAsync();
        try { return await new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, Content = content, PrimaryButtonText = confirm, CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary; }
        finally { dialogs.Release(); }
    }
    private void ShowPage(UIElement page)
    {
        foreach (var p in new UIElement[] { LibraryView, WorkspaceView, QueueView, SettingsView, ModelStoreView }) p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ApplySettings()
    {
        if (!string.IsNullOrWhiteSpace(settings.EngineRoot)) paths.EngineRoot = settings.EngineRoot;
        if (!string.IsNullOrWhiteSpace(settings.ModelRoot)) paths.ModelRoot = settings.ModelRoot;
        var theme = settings.Theme switch { "Dark" => ElementTheme.Dark, "Light" => ElementTheme.Light, _ => ElementTheme.Default };
        if (Root.RequestedTheme != theme) Root.RequestedTheme = theme;
        if (settings.ReduceMotion) { if (SystemBackdrop is not null) SystemBackdrop = null; }
        else if (SystemBackdrop is not MicaBackdrop) SystemBackdrop = new MicaBackdrop();
        AiProviderLabel.Text = (settings.ModelProvider == LanguageModelKind.Ollama ? "Ollama" : "内置 llama.cpp") + " · 内容仅在本机处理";
    }
    private void RefreshLibrary()
    {
        var search = LibrarySearch.Text.Trim(); var folder = FolderFilter.SelectedItem as string;
        var list = sessions.Where(s => (folder is null or "全部文件夹" || s.Folder == folder) && (search.Length == 0 || s.Title.Contains(search, StringComparison.OrdinalIgnoreCase) || s.Segments.Any(c => c.Text.Contains(search, StringComparison.OrdinalIgnoreCase)) || s.Summary.Contains(search, StringComparison.OrdinalIgnoreCase))).OrderByDescending(s => s.UpdatedAt).ToArray();
        LibraryList.ItemsSource = list; LibraryEmpty.Visibility = list.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var folders = new[] { "全部文件夹" }.Concat(sessions.Select(s => s.Folder).Distinct().Order()).ToArray();
        if (FolderFilter.ItemsSource is not string[] old || !folders.SequenceEqual(old))
        { loading = true; FolderFilter.ItemsSource = folders; FolderFilter.SelectedItem = folders.Contains(folder) ? folder : "全部文件夹"; loading = false; }
    }
    private async Task SaveCurrentAsync()
    {
        if (current is null || !dirty) return;
        var copy = Json.Read<MediaSession>(Json.Write(current)); dirty = false;
        await Task.Run(() => store.SaveSession(copy));
        current.UpdatedAt = copy.UpdatedAt;
    }
    private void Dirty() { if (current is null) return; dirty = true; saveTimer.Stop(); saveTimer.Start(); }
    private void TrackSegments()
    {
        if (current is null) return;
        foreach (var s in current.Segments) { s.PropertyChanged -= Segment_Changed; s.PropertyChanged += Segment_Changed; }
    }
    private void Segment_Changed(object? sender, PropertyChangedEventArgs e) { if (!selecting) Dirty(); }
    private void Snapshot()
    {
        if (current is null) return;
        var snapshot = Json.Write(current.Segments);
        if (undo.Count == 0 || undo.Peek() != snapshot) undo.Push(snapshot);
        if (undo.Count > 60) { var keep = undo.Take(50).Reverse().ToArray(); undo.Clear(); foreach (var s in keep) undo.Push(s); }
    }
    private async Task OpenSessionAsync(MediaSession session)
    {
        await SaveCurrentAsync(); current = session; selected = null; undo.Clear(); lastFollowId = null; waveform = [];
        SessionTitle.Text = session.Title; SessionMeta.Text = $"{session.Folder}  ·  {session.Segments.Count} 条字幕  ·  {session.DurationLabel}";
        AiOutput.Text = string.IsNullOrWhiteSpace(session.Summary) ? "转写完成后，在这里翻译、总结，或向这段内容提问。" : session.Summary;
        AiOutput.Visibility = Visibility.Visible; MindMapTree.Visibility = Visibility.Collapsed;
        SubtitleSearch.Text = ""; TrackSegments(); RefreshSubtitles(); ShowPage(WorkspaceView);
        selecting = true; OriginalEditor.Text = ""; TranslationEditor.Text = ""; selecting = false;
        Player.Source = null;
        var mediaPath = File.Exists(session.PlaybackPath) ? session.PlaybackPath : session.MediaPath;
        if (File.Exists(mediaPath)) Player.Source = MediaSource.CreateFromUri(new Uri(mediaPath));
        UpdateEditState();
    }
    private void RefreshSubtitles()
    {
        if (current is null) return;
        var search = SubtitleSearch.Text.Trim();
        var filtered = current.Segments.Where(s => search.Length == 0 || s.Text.Contains(search, StringComparison.OrdinalIgnoreCase) || s.Translation.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        // Replace the source once; ListView virtualizes containers even for large transcripts.
        SubtitleList.ItemsSource = filtered;
        SessionMeta.Text = $"{current.Folder}  ·  {current.Segments.Count} 条字幕  ·  {current.DurationLabel}";
    }
    private async Task<IReadOnlyList<string>> PickFilesAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        foreach (var ext in extensions.Length == 0 ? new[] { "*" } : extensions) picker.FileTypeFilter.Add(ext);
        var files = await picker.PickMultipleFilesAsync(); return files.Select(f => f.Path).ToArray();
    }
    private async Task<string?> PickSaveAsync(string name, string extension)
    {
        var picker = new FileSavePicker { SuggestedFileName = name };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeChoices.Add(extension.ToUpperInvariant() + " 文件", new List<string> { "." + extension });
        return (await picker.PickSaveFileAsync())?.Path;
    }
    private async Task ImportPathsAsync(IEnumerable<string> filePaths)
    {
        MediaSession? last = null;
        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var session = new MediaSession { Title = Path.GetFileNameWithoutExtension(path) };
            if (extension is ".srt" or ".vtt")
            {
                session.Segments = new(Subtitles.Parse(await File.ReadAllTextAsync(path)));
                if (session.Segments.Count == 0) throw new InvalidDataException("没有读到有效字幕，请检查 SRT/VTT 格式。");
                session.Duration = session.Segments.Max(s => s.End);
            }
            else if (extension == ".json")
            {
                session = Json.Read<MediaSession>(await File.ReadAllTextAsync(path)); session.Id = Guid.NewGuid().ToString("N");
                session.Segments = new(Subtitles.Repair(session.Segments));
            }
            else if (extension is ".txt" or ".md") session.Segments.Add(new() { Start = 0, End = 1, Text = await File.ReadAllTextAsync(path) });
            else
            {
                session.MediaPath = Path.GetFullPath(path);
                try { session.Duration = await new MediaTools(paths).ProbeAsync(path, CancellationToken.None); }
                catch (FileNotFoundException) { Status("媒体已导入；请先安装 FFmpeg 引擎再进行处理。"); }
                var subtitle = Path.ChangeExtension(path, ".srt");
                if (File.Exists(subtitle)) session.Segments = new(Subtitles.Parse(await File.ReadAllTextAsync(subtitle)));
            }
            await Task.Run(() => store.SaveSession(session)); sessions.Insert(0, session); last = session;
        }
        RefreshLibrary(); if (last is not null) { await OpenSessionAsync(last); Status("导入完成 · 原始媒体保留在原位置"); }
    }
    private async void Import_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () => await ImportPathsAsync(await PickFilesAsync()));
    private void Library_Click(object sender, RoutedEventArgs e) { RefreshLibrary(); ShowPage(LibraryView); }
    private void Queue_Click(object sender, RoutedEventArgs e) { RefreshJobs(); ShowPage(QueueView); }
    private void Settings_Click(object sender, RoutedEventArgs e) { BuildSettings(); ShowPage(SettingsView); }
    private async void Recent_Changed(object sender, SelectionChangedEventArgs e) { if (RecentList.SelectedItem is MediaSession s) await SafeAsync(() => OpenSessionAsync(s)); }
    private async void Library_ItemClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is MediaSession s) await SafeAsync(() => OpenSessionAsync(s)); }
    private void LibrarySearch_Changed(object sender, TextChangedEventArgs e) { if (sessions is not null) RefreshLibrary(); }
    private void FolderFilter_Changed(object sender, SelectionChangedEventArgs e) { if (!loading && sessions is not null) RefreshLibrary(); }
    private void SubtitleSearch_Changed(object sender, TextChangedEventArgs e) => RefreshSubtitles();
    private void Root_DragOver(object sender, DragEventArgs e) => e.AcceptedOperation = DataPackageOperation.Copy;
    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems)) await SafeAsync(async () => await ImportPathsAsync((await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>().Select(f => f.Path)));
    }
    private async void Link_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        var input = new TextBox { PlaceholderText = "YouTube / 播客 / 媒体 URL", MinWidth = 420 };
        var clipboard = Clipboard.GetContent(); if (clipboard.Contains(StandardDataFormats.Text)) { var text = await clipboard.GetTextAsync(); if (Uri.TryCreate(text, UriKind.Absolute, out _)) input.Text = text; }
        if (!await ConfirmAsync("导入在线链接", input, "下载媒体")) return;
        var session = new MediaSession { SourceUrl = input.Text.Trim(), Title = "在线媒体" };
        Status("正在下载在线媒体…", 0);
        session.MediaPath = await new MediaTools(paths).DownloadAsync(session.SourceUrl, paths.SessionDirectory(session.Id), CancellationToken.None, line => DispatcherQueue.TryEnqueue(() => Status(line)));
        session.Title = Path.GetFileNameWithoutExtension(session.MediaPath); session.Duration = await new MediaTools(paths).ProbeAsync(session.MediaPath, CancellationToken.None);
        store.SaveSession(session); sessions.Insert(0, session); RefreshLibrary(); await OpenSessionAsync(session); Status("在线媒体已导入");
    });
    private void RequireSession() { if (current is null) throw new InvalidOperationException("请先导入或打开一段媒体。"); }
    private void RequireSubtitles() { RequireSession(); if (current!.Segments.Count == 0) throw new InvalidOperationException("请先转写或导入字幕。"); }
    private async void Transcribe_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () => { RequireSession(); if (current!.Segments.Count > 0 && !await ConfirmAsync("重新转写", "新转写结果会替换当前字幕。已有字幕可先导出备份。", "重新转写")) return; await SaveCurrentAsync(); await scheduler.EnqueueAsync(current, "transcribe"); Status("已加入后台转写队列"); });
    private async void Batch_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        var list = LibraryList.SelectedItems.OfType<MediaSession>().Where(s => File.Exists(s.MediaPath)).ToArray();
        if (list.Length == 0) throw new InvalidOperationException("先在列表中按 Ctrl 或 Shift 选择需要转写的媒体。");
        if (list.Any(s => s.Segments.Count > 0) && !await ConfirmAsync("批量转写", "选中媒体的已有字幕将被新转写结果替换。", "加入队列")) return;
        foreach (var session in list) await scheduler.EnqueueAsync(session, "transcribe"); ShowPage(QueueView);
    });
    private Task NotifyJobAsync(JobRecord job, JobEvent? e)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (closing) { completion.SetResult(); return completion.Task; }
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (e?.Segments is { } segments)
                {
                    var session = sessions.FirstOrDefault(s => s.Id == job.SessionId);
                    if (session is not null)
                    {
                        if (job.Operation == "transcribe-range")
                        {
                            var start = job.Request!.RangeStart ?? 0; var end = job.Request.RangeEnd ?? double.MaxValue;
                            session.Segments = new(session.Segments.Where(s => s.End <= start || s.Start >= end).Concat(segments).OrderBy(s => s.Start));
                        }
                        else session.Segments = new(segments);
                        if (session.Duration == 0) session.Duration = segments.Select(s => s.End).DefaultIfEmpty().Max();
                        store.SaveSession(session);
                        if (current?.Id == session.Id) { current = session; TrackSegments(); RefreshSubtitles(); }
                    }
                }
                Status($"{job.Title} · {job.Message}", job.State == JobState.Running ? job.Progress : null);
                if (e?.Type == "completed" && e.Path is not null) Status("已保存：" + e.Path);
                RefreshJobs(); UpdateEditState();
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
            await Task.CompletedTask;
        })) completion.SetResult();
        return completion.Task;
    }
    private void RefreshJobs()
    {
        var selectedId = JobList.SelectedIndex >= 0 && JobList.SelectedIndex < jobs.Count ? jobs[JobList.SelectedIndex].Id : null;
        jobs = store.GetJobs(); jobLabels.Clear(); foreach (var job in jobs) jobLabels.Add(job.Display);
        if (selectedId is not null) JobList.SelectedIndex = jobs.FindIndex(j => j.Id == selectedId);
    }
    private void Job_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (JobList.SelectedIndex is var index && index >= 0 && index < jobs.Count)
        {
            var j = jobs[index]; JobDetails.Text = $"{j.Message}\n实际后端：{j.Backend}  设备：{j.Device}\n耗时：{j.ElapsedSeconds:0.00} 秒  实时率：{j.RealTimeFactor:0.000}\n更新：{j.UpdatedAt:yyyy-MM-dd HH:mm:ss}";
        }
    }
    private void CancelJob_Click(object sender, RoutedEventArgs e) { if (JobList.SelectedIndex is var i && i >= 0 && i < jobs.Count) scheduler.Cancel(jobs[i].Id); }
    private async void RetryJob_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () => { if (JobList.SelectedIndex is var i && i >= 0 && i < jobs.Count && jobs[i].State is not (JobState.Running or JobState.Queued)) await scheduler.RetryAsync(jobs[i]); });
    private void Subtitle_Changed(object sender, SelectionChangedEventArgs e)
    {
        selected = SubtitleList.SelectedItem as SubtitleSegment; selecting = true;
        OriginalEditor.Text = selected?.Text ?? ""; TranslationEditor.Text = selected?.Translation ?? ""; EditorTime.Text = selected?.TimeLabel ?? "选择字幕进行编辑";
        selecting = false;
        if (selected is not null && Player.MediaPlayer is not null) Player.MediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(selected.Start);
    }
    private void Editor_Focus(object sender, RoutedEventArgs e) { if (!selecting) Snapshot(); }
    private void Editor_Changed(object sender, TextChangedEventArgs e)
    {
        if (selecting || selected is null || current is null) return;
        selected.Text = OriginalEditor.Text; selected.Translation = TranslationEditor.Text; Dirty();
    }
    private void UpdateEditState()
    {
        var busy = aiBusy || current is not null && jobs.Any(j => j.SessionId == current.Id && j.State == JobState.Running);
        OriginalEditor.IsEnabled = !busy; TranslationEditor.IsEnabled = !busy;
    }
    private void PlaybackTick(object? sender, object e)
    {
        if (current is null || Player.MediaPlayer is null) return;
        var seconds = Player.MediaPlayer.PlaybackSession.Position.TotalSeconds;
        var cue = current.Segments.FirstOrDefault(s => s.Start <= seconds && s.End > seconds);
        floating?.SetSubtitle(cue is null ? "" : cue.Text + (cue.Translation.Length > 0 ? "\n" + cue.Translation : ""));
        if (cue is not null && cue.Id != lastFollowId && Player.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        { lastFollowId = cue.Id; SubtitleList.ScrollIntoView(cue, ScrollIntoViewAlignment.Default); }
    }
    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (current is null || Player.MediaPlayer is null) return;
        current.Duration = Player.MediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds; Dirty();
    }
    private async void Player_MediaFailed(string errorMessage) => await SafeAsync(async () =>
    {
        if (current is null || !File.Exists(current.MediaPath) || current.PlaybackPath.Length > 0) return;
        if (!await ConfirmAsync("当前格式无法直接播放", "可以在后台生成 720p 播放代理，原始文件保持原样。", "生成代理")) return;
        var session = current; Status("正在生成播放代理…", 0);
        session.PlaybackPath = await new MediaTools(paths).CreateProxyAsync(session.MediaPath, paths.SessionDirectory(session.Id), CancellationToken.None);
        store.SaveSession(session); if (current?.Id == session.Id) Player.Source = MediaSource.CreateFromUri(new Uri(session.PlaybackPath)); Status("播放代理已就绪");
    });
    private async void Ai_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSubtitles(); if (aiBusy) throw new InvalidOperationException("已有 AI 任务正在处理，请等待或停止。");
        var operation = (sender as FrameworkElement)?.Tag as string ?? "summary";
        var session = current!; var choice = SubtitleList.SelectedItems.OfType<SubtitleSegment>().OrderBy(s => s.Start).ToArray();
        var segments = choice.Length > 0 ? choice : session.Segments.ToArray();
        aiCancellation = new(); aiBusy = true; UpdateEditState(); var gateHeld = false;
        try
        {
            Status("等待本地计算资源…", 0);
            await AiService.GpuGate.WaitAsync(aiCancellation.Token); gateHeld = true;
            var runtimeSettings = Json.Read<AppSettings>(Json.Write(settings));
            provider?.Dispose(); provider = new LocalLanguageModelProvider(paths, runtimeSettings); var ai = new AiService(provider, runtimeSettings);
            AiOutput.Visibility = Visibility.Visible; MindMapTree.Visibility = Visibility.Collapsed;
            if (operation is "translate" or "correct")
            {
                Snapshot();
                async Task Checkpoint(int count) { store.SaveSession(session); Status($"{(operation == "translate" ? "翻译" : "校对")}字幕 {count}/{segments.Length}", (double)count / segments.Length); await Task.CompletedTask; }
                if (operation == "translate") await ai.TranslateAsync(segments, Checkpoint, aiCancellation.Token);
                else await ai.CorrectAsync(segments, Checkpoint, aiCancellation.Token);
                RefreshSubtitles();
            }
            else
            {
                AiOutput.Text = "";
                var watch = Stopwatch.StartNew(); var b = new StringBuilder();
                Task Delta(string text) { b.Append(text); if (watch.ElapsedMilliseconds > 80) { AiOutput.Text = b.ToString(); watch.Restart(); } return Task.CompletedTask; }
                string answer;
                if (operation == "ask")
                {
                    if (string.IsNullOrWhiteSpace(QuestionBox.Text)) throw new InvalidOperationException("请先输入问题。");
                    answer = await ai.AskAsync(session, QuestionBox.Text, Delta, aiCancellation.Token);
                }
                else answer = await ai.SummarizeAsync(session, operation, Delta, aiCancellation.Token);
                AiOutput.Text = answer;
                if (operation == "summary") session.Summary = answer;
                else if (operation == "chapters") session.Chapters = answer;
                else if (operation == "mindmap") { session.MindMapJson = AiService.ExtractJson(answer); ShowMindMap(session.MindMapJson); }
                store.SaveSession(session);
            }
            Status("本地 AI 处理完成");
        }
        finally { if (gateHeld) AiService.GpuGate.Release(); aiBusy = false; aiCancellation.Dispose(); aiCancellation = null; UpdateEditState(); }
    });
    private void ShowMindMap(string json)
    {
        using var document = JsonDocument.Parse(json); MindMapTree.RootNodes.Clear();
        TreeViewNode Node(JsonElement element, int depth)
        {
            var node = new TreeViewNode { Content = element.GetProperty("title").GetString(), IsExpanded = depth < 2 };
            if (depth < 8 && element.TryGetProperty("children", out var children)) foreach (var child in children.EnumerateArray().Take(150)) node.Children.Add(Node(child, depth + 1));
            return node;
        }
        MindMapTree.RootNodes.Add(Node(document.RootElement, 0)); MindMapTree.Visibility = Visibility.Visible; AiOutput.Visibility = Visibility.Collapsed;
    }
    private void StopAi_Click(object sender, RoutedEventArgs e) => aiCancellation?.Cancel();
    private void AiToggle_Click(object sender, RoutedEventArgs e) { var show = AiToggle.IsChecked == true; AiPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed; AiColumn.Width = new GridLength(show ? 310 : 0); }
    private void Workspace_SizeChanged(object sender, SizeChangedEventArgs e) { if (e.NewSize.Width < 760 && AiToggle.IsChecked == true) { AiToggle.IsChecked = false; AiToggle_Click(this, new()); } }
    private async void Export_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSession(); var extension = (string)((FrameworkElement)sender).Tag; var session = current!;
        if (extension == "svg" && session.MindMapJson.Length == 0) throw new InvalidOperationException("请先生成思维导图。");
        var output = await PickSaveAsync(session.Title, extension); if (output is null) return;
        var text = extension == "svg" ? Subtitles.MindMapSvg(session.MindMapJson, session.Title) : Subtitles.Export(session, extension);
        await File.WriteAllTextAsync(output, text, new UTF8Encoding(false)); Status("已导出：" + output);
    });
    private async void VideoExport_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSession(); var session = current!; var dub = (sender as FrameworkElement)?.Tag as string == "dub" ? Path.Combine(paths.SessionDirectory(session.Id), "dub.wav") : null;
        if (dub is not null && !File.Exists(dub)) throw new FileNotFoundException("请先在更多菜单中生成对齐配音。");
        var output = await PickSaveAsync(session.Title + "-字幕", "mp4"); if (output is null) return;
        Status("正在导出视频…", 0); await new MediaTools(paths).ExportVideoAsync(session, output, dub, true, CancellationToken.None); Status("视频已导出：" + output);
    });
    private async void ClipExport_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSubtitles(); var cues = SubtitleList.SelectedItems.OfType<SubtitleSegment>().ToArray(); if (cues.Length == 0) throw new InvalidOperationException("先选择要导出的字幕片段。");
        var output = await PickSaveAsync(current!.Title + "-片段", "mp4"); if (output is not null) { await new MediaTools(paths).ExportClipAsync(current.MediaPath, output, cues.Min(s => s.Start), cues.Max(s => s.End), CancellationToken.None); Status("片段已导出：" + output); }
    });
    private async void Rename_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSession(); var title = new TextBox { Header = "标题", Text = current!.Title }; var folder = new TextBox { Header = "文件夹", Text = current.Folder }; var content = new StackPanel { Spacing = 12 }; content.Children.Add(title); content.Children.Add(folder);
        if (await ConfirmAsync("整理媒体", content, "保存")) { current.Title = string.IsNullOrWhiteSpace(title.Text) ? "未命名媒体" : title.Text.Trim(); current.Folder = string.IsNullOrWhiteSpace(folder.Text) ? "收件箱" : folder.Text.Trim(); Dirty(); await SaveCurrentAsync(); SessionTitle.Text = current.Title; RefreshLibrary(); }
    });
    private async void Associate_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () => { RequireSession(); var files = await PickFilesAsync(); if (files.Count > 0) { current!.MediaPath = files[0]; current.PlaybackPath = ""; Dirty(); await SaveCurrentAsync(); await OpenSessionAsync(current); } });
    private async void Delete_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSession(); if (!await ConfirmAsync("从媒体库移除？", "会移除本应用中的记录；原始媒体文件保留。", "移除")) return;
        var session = current!; current = null; dirty = false; Player.Source = null; store.DeleteSession(session.Id); sessions.Remove(session); RefreshLibrary(); ShowPage(LibraryView);
    });
    private void Repair_Click(object sender, RoutedEventArgs e) { if (current is null) return; Snapshot(); current.Segments = new(Subtitles.Repair(current.Segments)); TrackSegments(); RefreshSubtitles(); Dirty(); }
    private async void SeparateBilingual_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSubtitles(); if (!await ConfirmAsync("拆分双语字幕", "把每条字幕的第一行保留为原文，其余行移到译文。已有译文不受影响。", "拆分")) return;
        Snapshot(); foreach (var s in current!.Segments.Where(s => s.Translation.Length == 0 && s.Text.Contains('\n'))) { var lines = s.Text.Split('\n'); s.Text = lines[0]; s.Translation = string.Join('\n', lines.Skip(1)); }
        Dirty(); RefreshSubtitles();
    });
    private async void EditTiming_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        if (selected is null) return;
        var start = new NumberBox { Header = "开始（秒）", Value = selected.Start, Minimum = 0, SmallChange = 0.1 };
        var end = new NumberBox { Header = "结束（秒）", Value = selected.End, Minimum = 0, SmallChange = 0.1 };
        var speaker = new TextBox { Header = "说话人", Text = selected.Speaker }; var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(start); panel.Children.Add(end); panel.Children.Add(speaker);
        if (await ConfirmAsync("字幕时间与说话人", panel, "保存")) { if (!double.IsFinite(start.Value) || !double.IsFinite(end.Value) || end.Value <= start.Value) throw new ArgumentException("结束时间必须大于开始时间。"); Snapshot(); selected.Start = start.Value; selected.End = end.Value; selected.Speaker = speaker.Text; Dirty(); RefreshSubtitles(); }
    });
    private void Split_Click(object sender, RoutedEventArgs e) { if (selected is null || current is null) return; Snapshot(); var position = Player.MediaPlayer?.PlaybackSession.Position.TotalSeconds ?? 0; if (position <= selected.Start || position >= selected.End) position = (selected.Start + selected.End) / 2; var (left, right) = Subtitles.Split(selected, position); var index = current.Segments.IndexOf(selected); current.Segments.RemoveAt(index); current.Segments.Insert(index, right); current.Segments.Insert(index, left); TrackSegments(); RefreshSubtitles(); Dirty(); }
    private void Merge_Click(object sender, RoutedEventArgs e) { if (selected is null || current is null) return; var index = current.Segments.IndexOf(selected); if (index < 0 || index >= current.Segments.Count - 1) return; Snapshot(); var merged = Subtitles.Merge(selected, current.Segments[index + 1]); current.Segments.RemoveAt(index + 1); current.Segments[index] = merged; TrackSegments(); RefreshSubtitles(); Dirty(); }
    private void Undo_Click(object sender, RoutedEventArgs e) { if (current is null || undo.Count == 0 || aiBusy) return; current.Segments = new(Json.Read<List<SubtitleSegment>>(undo.Pop())); TrackSegments(); RefreshSubtitles(); Dirty(); }
    private async void Replace_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSubtitles(); var find = new TextBox { Header = "查找", Text = SubtitleSearch.Text }; var replacement = new TextBox { Header = "替换为" }; var content = new StackPanel { Spacing = 12 }; content.Children.Add(find); content.Children.Add(replacement);
        if (await ConfirmAsync("替换原文和译文", content, "替换全部") && find.Text.Length > 0) { Snapshot(); foreach (var s in current!.Segments) { s.Text = s.Text.Replace(find.Text, replacement.Text, StringComparison.Ordinal); s.Translation = s.Translation.Replace(find.Text, replacement.Text, StringComparison.Ordinal); } Dirty(); RefreshSubtitles(); }
    });
    private async void Subtitle_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Space && Player.MediaPlayer is not null) { if (Player.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) Player.MediaPlayer.Pause(); else Player.MediaPlayer.Play(); e.Handled = true; }
        if (e.Key == VirtualKey.Delete && current is not null && !aiBusy) { Snapshot(); foreach (var s in SubtitleList.SelectedItems.OfType<SubtitleSegment>().ToArray()) current.Segments.Remove(s); RefreshSubtitles(); Dirty(); e.Handled = true; }
        if (e.Key == VirtualKey.F2 && selected is not null) await SafeAsync(async () => { if (await ConfirmAsync("重转写所选片段", "此片段的字幕会由新转写结果替换。", "重转写")) await scheduler.EnqueueRangeAsync(current!, selected.Start, selected.End); });
    }
    private async void Diarize_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () => { RequireSubtitles(); await scheduler.EnqueueAsync(current!, "diarize"); });
    private async void Dub_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () => { RequireSubtitles(); await scheduler.EnqueueAsync(current!, "tts"); Status("对齐配音已加入队列"); });
    private async void Timeline_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        TimelineBorder.Visibility = TimelineBorder.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (TimelineBorder.Visibility == Visibility.Visible && current is not null && waveform.Length == 0 && File.Exists(current.MediaPath)) { var id = current.Id; var data = await new MediaTools(paths).WaveformAsync(current.MediaPath, paths.SessionDirectory(id), CancellationToken.None); if (current?.Id == id) waveform = data; DrawTimeline(); }
    });
    private void Timeline_SizeChanged(object sender, SizeChangedEventArgs e) => DrawTimeline();
    private void DrawTimeline()
    {
        TimelineCanvas.Children.Clear(); var width = TimelineCanvas.ActualWidth; var height = TimelineCanvas.ActualHeight;
        if (waveform.Length == 0 || width <= 0) return;
        var count = Math.Min(400, waveform.Length);
        for (var i = 0; i < count; i++) { var h = Math.Max(2, waveform[i * waveform.Length / count] * (height - 8)); var bar = new Rectangle { Width = Math.Max(1, width / count - 1), Height = h, RadiusX = 1, RadiusY = 1, Fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] }; Canvas.SetLeft(bar, i * width / count); Canvas.SetTop(bar, (height - h) / 2); TimelineCanvas.Children.Add(bar); }
    }
    private void Timeline_Pressed(object sender, PointerRoutedEventArgs e) { if (current is not null && Player.MediaPlayer is not null && TimelineCanvas.ActualWidth > 0) Player.MediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(e.GetCurrentPoint(TimelineCanvas).Position.X / TimelineCanvas.ActualWidth, 0, 1) * current.Duration); }
    private void Floating_Click(object sender, RoutedEventArgs e) { if (floating is not null) { floating.Close(); floating = null; return; } floating = new FloatingSubtitleWindow(); floating.Closed += (_, _) => floating = null; floating.Activate(); }
    private async void Record_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        if (recorder is not null)
        {
            var output = await recorder.StopAsync(); recorder.Dispose(); recorder = null; await ImportPathsAsync([output]); Status("录音已保存"); return;
        }
        var mode = new ComboBox { ItemsSource = new[] { "系统音频", "麦克风", "系统音频 + 麦克风" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (!await ConfirmAsync("开始录音", mode, "开始")) return;
        var directory = Path.Combine(paths.DataRoot, "recordings"); Directory.CreateDirectory(directory);
        recorder = new AudioRecorder(directory, mode.SelectedIndex, paths); recorder.Start(); Status("● 正在录音 · 再次点击左侧「录音」停止并保存");
    });
    private async void Integration_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSubtitles(); var tag = (sender as FrameworkElement)?.Tag as string;
        if (tag == "obsidian") Status("已保存：" + await Integrations.ExportObsidianAsync(current!, settings.ObsidianFolder, CancellationToken.None));
        else if (await ConfirmAsync("发送到 Notion", "这次操作会将当前转写和总结发送到你配置的 Notion 页面。", "发送"))
        {
            var token = SecretVault.Get("notion"); Status("正在发送到 Notion…"); var url = await Integrations.ExportNotionAsync(current!, settings.NotionParentPage, token, CancellationToken.None); Status("Notion 页面已创建：" + url);
        }
    });
    private void Window_Closed(object sender, WindowEventArgs args)
    {
        Player.MediaPlayer?.Dispose();
    }
}
