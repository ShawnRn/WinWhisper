using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private TaskCompletionSource<ContentDialogResult>? transcriptReviewCompletion;
    private void CompleteTranscriptReview(ContentDialogResult decision = ContentDialogResult.None)
    {
        var completion = transcriptReviewCompletion;
        transcriptReviewCompletion = null;
        if (completion is null) return;
        TranscriptReviewHost.Content = null; TranscriptReviewHost.Visibility = Visibility.Collapsed;
        TranscriptSearchBar.Visibility = SubtitleList.Visibility = Visibility.Visible;
        UpdateEditState(); completion.TrySetResult(decision);
    }
    private void ChatEditMode_Changed(object sender, RoutedEventArgs e)
    {
        if (QuestionBox is null) return;
        QuestionBox.PlaceholderText = ChatEditMode.IsChecked == true ? "例如：修正术语、去掉 uh / um，再优化英文断句…" : "问问这段内容…";
    }
    private void RequireTranscriptIdle()
    {
        RequireSubtitles();
        if (aiBusy || transcriptReviewCompletion is not null || jobs.Any(j => j.SessionId == current!.Id && j.State is JobState.Running or JobState.Queued))
            throw new InvalidOperationException("请等待当前转写 / AI 任务完成后修改字幕。");
    }
    private async void RemoveFillers_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireTranscriptIdle(); var session = current!; var before = session.Segments.Select(c => c.Copy()).ToArray();
        var after = TranscriptEditing.RemoveFillers(before);
        await PreviewTranscriptAsync(session, before, after, "清理英语语气词", "只清理独立的 uh / um 及拉长形式，不删除 like / well 等有实际含义的词。原始音频保留。");
    });
    private async Task<bool> PreviewTranscriptAsync(MediaSession session, IReadOnlyList<SubtitleSegment> before, IReadOnlyList<SubtitleSegment> after, string title, string details, CancellationToken ct = default, TranscriptRevision? revision = null, bool readOnly = false)
    {
        if (transcriptReviewCompletion is not null) throw new InvalidOperationException("请先完成当前字幕提案的审阅。");
        if (current?.Id != session.Id) throw new InvalidOperationException("当前媒体已切换，未应用字幕修改。");
        var expected = Json.Write(before);
        var stale = Json.Write(session.Segments) != expected || revision is not null && store.GetTranscriptHead(session.Id) != revision.ParentId;
        if (stale && revision is null) throw new InvalidOperationException("字幕在处理期间已变化，请重新生成预览。");
        if (Json.Write(before) == Json.Write(after)) { Status("字幕无需修改。"); return false; }
        revision ??= store.CreateTranscriptProposal(session, before, after, title);
        var lostTranslations = before.Count(c => c.Translation.Length > 0 && !after.Any(a => a.Id == c.Id && a.Translation == c.Translation));
        var body = CreateTranscriptPreview(before, after, details + (stale && !readOnly ? "\n此提案的基准已过期，只能拒绝或稍后处理；请根据新版本重新生成。" : ""), lostTranslations);
        var heading = (StackPanel)((ScrollViewer)body.Children[0]).Content;
        heading.Children.Insert(0, new TextBlock { Text = title + " · " + revision.Id[..8], FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (revision.Events.Count > 0)
        {
            var log = new Expander { Header = $"Agent 记录 · {revision.Events.Count} 步", HorizontalAlignment = HorizontalAlignment.Stretch, Content = new ScrollViewer { MaxHeight = 110, Content = new TextBlock { Text = string.Join("\n", revision.Events.Select(e => $"{e.Sequence}. {e.Tool} · {e.State} · {e.Message}")), FontSize = 12, TextWrapping = TextWrapping.Wrap } } };
            Grid.SetRow(log, 2); body.Children.Add(log);
        }
        var actions = new Grid { ColumnSpacing = 6 };
        for (var i = 0; i < 3; i++) actions.ColumnDefinitions.Add(new ColumnDefinition());
        var allow = new Button { Content = "允许修改", IsEnabled = !stale, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var reject = new Button { Content = "拒绝修改" }; var later = new Button { Content = "稍后" };
        if (readOnly) { allow.Visibility = reject.Visibility = Visibility.Collapsed; actions.ColumnDefinitions[0].Width = actions.ColumnDefinitions[1].Width = new GridLength(0); later.Content = "关闭记录"; }
        var buttons = new[] { allow, reject, later };
        for (var i = 0; i < buttons.Length; i++) { buttons[i].HorizontalAlignment = HorizontalAlignment.Stretch; buttons[i].FontSize = 13; buttons[i].Padding = new Thickness(6, 8, 6, 8); Grid.SetColumn(buttons[i], i); actions.Children.Add(buttons[i]); }
        allow.Click += (_, _) => CompleteTranscriptReview(ContentDialogResult.Primary);
        reject.Click += (_, _) => CompleteTranscriptReview(ContentDialogResult.Secondary);
        later.Click += (_, _) => CompleteTranscriptReview();
        Grid.SetRow(actions, 3); body.Children.Add(actions);
        var completion = new TaskCompletionSource<ContentDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        transcriptReviewCompletion = completion;
        TranscriptSearchBar.Visibility = SubtitleList.Visibility = Visibility.Collapsed;
        TranscriptReviewHost.Content = body; TranscriptReviewHost.Visibility = Visibility.Visible;
        if (aiBusy) { aiWatch.Stop(); AiActivityRing.IsActive = false; AiActivityRing.Visibility = AiActivityProgress.Visibility = Visibility.Collapsed; SetAiFeedback("修改提案已保存 · 请在字幕区审阅"); }
        UpdateEditState();
        using var cancellation = ct.Register(() => DispatcherQueue.TryEnqueue(() => { if (ReferenceEquals(transcriptReviewCompletion, completion)) CompleteTranscriptReview(); }));
        var decision = await completion.Task;
        if (readOnly) return false;
        if (decision == ContentDialogResult.Secondary) { store.DecideTranscriptProposal(session, revision.Id, false); Status("提案已拒绝，原字幕保留。"); return false; }
        if (decision != ContentDialogResult.Primary) { Status("提案已保存，可从「字幕版本与提案」继续审阅。"); return false; }
        ct.ThrowIfCancellationRequested();
        CommitTranscriptChanges(session, before, after, revision.Id);
        Status($"{title}已应用 · {after.Count} 条字幕 · Ctrl+Z 撤销"); return true;
    }
    private Grid CreateTranscriptPreview(IReadOnlyList<SubtitleSegment> before, IReadOnlyList<SubtitleSegment> after, string details, int lostTranslations)
    {
        var body = new Grid { RowSpacing = 10 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(new TextBlock { Text = $"{before.Count} → {after.Count} 条字幕 · 应用后可 Ctrl+Z 撤销。\n{details}" + (lostTranslations > 0 ? $"\n{lostTranslations} 条旧译文因原文 / 边界变化将清除，需重新翻译。" : ""), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new ScrollViewer { MaxHeight = 150, Content = heading });
        ListView View(IReadOnlyList<SubtitleSegment> cues) => new() { ItemsSource = cues, ItemTemplate = (DataTemplate)Root.Resources["TranscriptPreviewTemplate"], SelectionMode = ListViewSelectionMode.None };
        var tabs = new Pivot { SelectedIndex = 0 };
        tabs.Items.Add(new PivotItem { Header = "差异", Content = new ListView { ItemsSource = TranscriptDiff.Build(before, after), ItemTemplate = (DataTemplate)Root.Resources["TranscriptDiffTemplate"], SelectionMode = ListViewSelectionMode.None } });
        tabs.Items.Add(new PivotItem { Header = "修改前", Content = View(before) }); tabs.Items.Add(new PivotItem { Header = "修改后", Content = View(after) }); Grid.SetRow(tabs, 1); body.Children.Add(tabs);
        return body;
    }
    private void CommitTranscriptChanges(MediaSession session, IReadOnlyList<SubtitleSegment> before, IReadOnlyList<SubtitleSegment> after, string? revisionId = null)
    {
        if (current?.Id != session.Id || Json.Write(session.Segments) != Json.Write(before)) throw new InvalidOperationException("媒体或字幕已变化，未覆盖新内容。");
        var revision = revisionId is null ? store.CreateTranscriptProposal(session, before, after, "字幕修改") : store.GetTranscriptVersion(session.Id, revisionId);
        store.DecideTranscriptProposal(session, revision.Id, true);
        Snapshot(); ApplySubtitleSnapshot(session, revision.After); TrackSegments(); RefreshSubtitles(); store.SaveSession(session);
    }
    private async Task EditTranscriptAsync(string instruction)
    {
        if (instruction.Length is 0 or > 4000) throw new ArgumentException("修改要求需为 1–4000 字。");
        RequireTranscriptIdle(); var session = current!; var before = session.Segments.Select(c => c.Copy()).ToArray();
        var cleanup = new CheckBox { Content = "清理独立的 uh / um", IsChecked = true };
        var resegment = new CheckBox { Content = "按真实词时间戳与音频停顿重新断句", IsChecked = true };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = $"本地 AI 将逐批读取全部 {before.Length} 条字幕，按照要求提出修改；不会发送音视频。结果先预览，再应用。\n\n要求：{instruction}", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(cleanup); body.Children.Add(resegment);
        body.Children.Add(new TextBlock { Text = "纠错改变单词后，该条保留原时间范围；只有词时间戳可靠的原文可重新切分。变化的原文会清除对应旧译文。", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        if (!await ConfirmAsync("修改完整转写", body, "生成修改预览") || current?.Id != session.Id) return;
        var response = BeginChatResponse(session, "ask", instruction); response.Status = "读取完整转写…";
        var harness = new TranscriptAgentHarness();
        var run = store.CreateTranscriptProposal(session, before, [], instruction, status: "Generating", kind: "Agent");
        aiBusy = true; aiCancellation = new(); var token = aiCancellation.Token; var gateHeld = false;
        UpdateEditState(); BeginAiFeedback(null, "edit", settings.SummaryModel);
        try
        {
            var source = cleanup.IsChecked == true ? TranscriptEditing.RemoveFillers(before) : before.ToList();
            async Task Report(TranscriptAgentEvent e) { SetAiFeedback($"{e.Tool} · {e.Message}"); run.Events = harness.Events.ToList(); store.UpdateTranscriptRun(run); await Task.CompletedTask; }
            if (cleanup.IsChecked == true) await harness.RecordAsync("remove_fillers", "completed", "已清理英语 uh / um，原音频保留", Report);
            await AiService.GpuGate.WaitAsync(token); gateHeld = true;
            using var model = new LocalLanguageModelProvider(paths, Json.Read<AppSettings>(Json.Write(settings)));
            var proposed = await harness.RunAsync(model, settings, source, instruction, Report, token);
            AiService.GpuGate.Release(); gateHeld = false;
            if (resegment.IsChecked == true)
            {
                var pauses = File.Exists(session.MediaPath) ? await new MediaTools(paths).DetectSilencesAsync(session.MediaPath, token) : [];
                proposed = await Task.Run(() => SentenceSegmentation.Optimize(proposed, pauses), token);
                await harness.RecordAsync("segment_transcript", "completed", $"真实词边界重分为 {proposed.Count} 条字幕", Report);
            }
            run.After = proposed.Select(s => s.Copy()).ToArray(); run.Events = harness.Events.ToList(); run.Status = TranscriptRevision.Hash(before) == TranscriptRevision.Hash(proposed) ? "NoChanges" : "Pending"; store.UpdateTranscriptRun(run);
            token.ThrowIfCancellationRequested(); response.Content = "完整转写已读取，字幕修改已生成，等待预览。"; response.Status = "等待应用";
            var applied = await PreviewTranscriptAsync(session, before, proposed, "AI 修改字幕", "只根据可靠词时间戳重新切分；文字纠错未能保持词对齐的条目保留原时间。", token, run);
            response.Content = applied ? $"已应用字幕修改：{before.Length} → {proposed.Count} 条。可 Ctrl+Z 撤销。" : "本次未应用修改，原字幕保留。";
            response.Status = applied ? "已应用 · " + run.Id[..8] : "未应用 · 版本历史可查看"; SetAiFeedback(response.Content, 1); store.SaveSession(session);
        }
        catch (OperationCanceledException) { if (run.Status == "Generating") { run.Status = "Cancelled"; store.UpdateTranscriptRun(run); } response.Content = "已停止，原字幕保留。"; response.Status = "已停止"; }
        catch (Exception ex) { if (run.Status == "Generating") { run.Status = "Failed"; store.UpdateTranscriptRun(run); } response.Content = "未修改字幕：" + ex.Message; response.Status = "生成失败"; }
        finally { if (gateHeld) AiService.GpuGate.Release(); aiBusy = false; aiCancellation.Dispose(); aiCancellation = null; EndAiFeedback(); UpdateEditState(); store.SaveSession(session); }
    }
}
