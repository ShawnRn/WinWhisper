using System.Diagnostics;
using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private async Task RunUiSelfTestAsync()
    {
        var result = new Dictionary<string, object>();
        try
        {
            // Invoked only by an explicit development argument with a separate --data-root.
            if (!Environment.GetCommandLineArgs().Any(a => a.StartsWith("--data-root="))) throw new InvalidOperationException("UI 自测必须指定隔离的数据目录。");
            ollamaModels = ["test-translation", "test-summary-a", "test-summary-b"];
            var navigationPassed = true;
            foreach (var (item, page) in new (NavigationViewItem, UIElement)[] { (NavQueue, QueueView), (NavRecord, RecordView), (NavLink, LinkView), (NavModels, ModelStoreView), (NavSettings, SettingsView), (NavLibrary, LibraryView) })
            {
                Sidebar.SelectedItem = item; await Task.Delay(40);
                navigationPassed &= item.IsSelected && page.Visibility == Visibility.Visible;
            }
            result["navigationTracksAllSixPages"] = navigationPassed;
            result["navigationDoesNotStartRecordingOrDownload"] = recorder is null && linkCancellation is null;
            result["bottomStatusBarRemoved"] = Root.RowDefinitions.Count == 2;
            var wavePath = Path.Combine(paths.DataRoot, "playback-fixture.wav");
            using (var wave = new BinaryWriter(File.Create(wavePath)))
            {
                wave.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); wave.Write(32036); wave.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                wave.Write(16); wave.Write((short)1); wave.Write((short)1); wave.Write(16000); wave.Write(32000); wave.Write((short)2); wave.Write((short)16);
                wave.Write(System.Text.Encoding.ASCII.GetBytes("data")); wave.Write(32000); wave.Write(new byte[32000]);
            }
            var fixture = new MediaSession { Title = "界面回归样本", MediaPath = wavePath, Segments = [new() { Start = 0, End = 1, Text = "界面回归测试。" }] };
            await OpenSessionAsync(fixture);
            AiToggle.IsChecked = true; AiToggle_Click(this, new()); await Task.Delay(120);
            for (var i = 0; i < 100 && Player.MediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds == 0; i++) await Task.Delay(20);
            result["nativePlaybackOpensRealMedia"] = Player.MediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds > 0.9;
            result["workspaceSelectsLibrary"] = NavLibrary.IsSelected;
            TranscriptionFlyout.ShowAt(TranscriptionSettingsButton); await Task.Delay(120);
            DependencyObject? flyoutParent = TranscriptionSettingsPanel;
            while (flyoutParent is not null && flyoutParent is not FlyoutPresenter) flyoutParent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(flyoutParent);
            result["transcriptionSettingsFitFlyout"] = flyoutParent is FlyoutPresenter presenter && presenter.ActualWidth >= TranscriptionSettingsPanel.ActualWidth;
            TranscriptionFlyout.Hide();
            await AiService.GpuGate.WaitAsync();
            try
            {
                var action = AiActions.Children.OfType<Button>().First(b => b.Tag as string == "summary");
                Ai_Click(action, new()); await Task.Delay(80);
                result["aiClickImmediatelyShowsBusy"] = aiBusy && AiActivityRing.IsActive && StopAiButton.IsEnabled && !action.IsEnabled && AiActivityText.Text.Contains("等待");
                StopAi_Click(StopAiButton, new());
                for (var i = 0; i < 50 && aiBusy; i++) await Task.Delay(20);
                result["aiCancelRestoresControls"] = !aiBusy && !AiActivityRing.IsActive && action.IsEnabled && !StopAiButton.IsEnabled && AiActivityText.Text.Contains("已停止");
            }
            finally { AiService.GpuGate.Release(); }
            var job = new JobRecord { SessionId = fixture.Id, State = JobState.Running, Progress = 0.42, Backend = "Cuda", Device = "Fixture GPU", Message = "片段 2/6", StartedAt = DateTimeOffset.Now, Request = new("fixture", fixture.Id, "transcribe", "", paths.SessionDirectory(fixture.Id), new() { TranscriptionModelPath = "fixture-model.bin", Language = "zh" }) };
            job.Operation = "transcribe"; store.SaveJob(job); RefreshJobs(); RefreshTranscriptionProgress();
            result["workspaceProgressIsVisibleAndScoped"] = TranscriptionProgressCard.Visibility == Visibility.Visible && Math.Abs(TranscriptionProgress.Value - 42) < 0.01 && TranscriptionRunDetails.Text.Contains("fixture-model") && TranscriptionRunDetails.Text.Contains("Fixture GPU") && !StartTranscribeButton.IsEnabled;
            job.State = JobState.Completed; job.Progress = 1; store.SaveJob(job); RefreshJobs(); RefreshTranscriptionProgress();
            result["workspaceCompletionRestoresStart"] = StartTranscribeButton.IsEnabled && CancelTranscription.Visibility == Visibility.Collapsed && TranscriptionStage.Text.StartsWith("转写完成") && TranscriptionProgress.Visibility == Visibility.Collapsed;
            result["playbackControlsDoNotOverlayVideo"] = !Player.AreTransportControlsEnabled;
            WorkspaceView.UpdateLayout();
            var videoOrigin = Player.TransformToVisual(WorkspaceView).TransformPoint(new Windows.Foundation.Point());
            var controlsOrigin = PlaybackControls.TransformToVisual(WorkspaceView).TransformPoint(new Windows.Foundation.Point());
            result["playbackHasUsableHeightAndSeparateControls"] = Player.ActualHeight >= 200 && controlsOrigin.Y >= videoOrigin.Y + Player.ActualHeight;
            result["chatHistoryHasUserAndAssistant"] = fixture.Chat.Any(t => t.Role == "user") && fixture.Chat.Any(t => t.Role == "assistant") && ChatMessages.ItemsSource == fixture.Chat;
            var markdown = new NativeMarkdownView { Text = "## 标题\n\n**加粗**与列表：\n\n- 第一项\n- 第二项\n\n```csharp\nvar a = 1;\n```\n\n| 列 | 值 |\n|---|---|\n| A | 1 |" };
            result["nativeMarkdownRendersBlocks"] = markdown.Content is StackPanel markdownBlocks && markdownBlocks.Children.Count >= 4;
            var cjkMarkdown = new NativeMarkdownView { Text = "专家**「无师」**分享**理性**的观点。" };
            var cjkParagraph = ((RichTextBlock)((StackPanel)cjkMarkdown.Content).Children[0]).Blocks.OfType<Microsoft.UI.Xaml.Documents.Paragraph>().Single();
            result["nativeMarkdownHandlesChineseQuotes"] = cjkParagraph.Inlines.OfType<Microsoft.UI.Xaml.Documents.Bold>().Count() == 2 && !cjkParagraph.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().Any(r => r.Text.Contains("**"));
            var timestamps = new NativeMarkdownView { Text = "引用 [00:00:38.530] 和 01:09，代码 `00:00:28.060`。" };
            var timestampParagraph = ((RichTextBlock)((StackPanel)timestamps.Content).Children[0]).Blocks.OfType<Microsoft.UI.Xaml.Documents.Paragraph>().Single();
            result["timestampReferencesBecomeLinksExceptCode"] = timestampParagraph.Inlines.OfType<Microsoft.UI.Xaml.Documents.Hyperlink>().Count() == 2;
            var streamTurn = fixture.Chat.Last(); followChat = true;
            streamTurn.Content = string.Join("\n\n", Enumerable.Range(0, 45).Select(i => $"流式段落 {i}：这是一段用于真实布局的长文本。"));
            for (var i = 0; i < 8; i++) { streamTurn.Content += $"\n\n新增内容 {i}。"; FollowChat(); await Task.Delay(60); }
            await Task.Delay(160);
            result["streamingFollowsLatestAfterLayout"] = chatScroll is not null && chatScroll.ScrollableHeight > 100 && chatScroll.ScrollableHeight - chatScroll.VerticalOffset < 3;
            result["chatScrollDiagnostics"] = $"visible={AiPanel.Visibility}; attached={chatScroll is not null}; follow={followChat}; offset={chatScroll?.VerticalOffset}; extent={chatScroll?.ScrollableHeight}; viewport={chatScroll?.ViewportHeight}";
            chatScroll?.ChangeView(null, 0, null, true); await Task.Delay(120);
            var readingOffset = chatScroll?.VerticalOffset ?? -1;
            streamTurn.Content += "\n\n阅读前文时新增的回复。"; FollowChat(); await Task.Delay(160);
            result["streamingPreservesReadingPosition"] = !followChat && Math.Abs((chatScroll?.VerticalOffset ?? -100) - readingOffset) < 3;
            chatScroll?.ChangeView(null, chatScroll.ScrollableHeight, null, true); await Task.Delay(120);
            streamTurn.Content += "\n\n回到底部后继续生成。"; FollowChat(); await Task.Delay(160);
            result["streamingResumesAtBottom"] = followChat && chatScroll is not null && chatScroll.ScrollableHeight - chatScroll.VerticalOffset < 3;
            ArrangeWorkspace(); WorkspaceView.UpdateLayout();
            result["wideWorkspaceUsesColumnsAndVideoAspect"] = mediaLayout == "columns" && Grid.GetColumn(TranscriptPane) == 1 && Math.Abs(Player.ActualWidth / Player.ActualHeight - videoAspect) < .05 && SubtitleList.ActualHeight > Player.ActualHeight;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1320, 900)); await Task.Delay(180); ArrangeWorkspace();
            result["mediumWorkspaceReflowsEditor"] = mediaLayout == "compact" && Grid.GetRow(TranscriptPane) == 1 && Grid.GetColumn(SubtitleEditorPane) == 1;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 900)); await Task.Delay(180); ArrangeWorkspace();
            result["narrowWorkspaceStacksWithoutVideoOverflow"] = mediaLayout == "stacked" && VideoPane.ActualWidth <= MediaWorkspace.ActualWidth + 1;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 980)); AiToggle.IsChecked = true; AiToggle_Click(this, new()); await Task.Delay(180);
            sessions.Add(fixture); ShowPage(LibraryView); RefreshLibrary(); LibraryList.SelectedItems.Add(fixture);
            LibraryMode_Click(LibraryGridMode, new());
            result["libraryGridPersistsModeAndSelection"] = LibraryGrid.Visibility == Visibility.Visible && LibraryGrid.SelectedItems.Contains(fixture) && store.LoadSettings().LibraryViewMode == "Grid";
            LibrarySearch.Text = fixture.Title;
            result["libraryViewsShareFilters"] = ReferenceEquals(LibraryGrid.ItemsSource, LibraryList.ItemsSource) && LibraryGrid.Items.Count == 1;
            LibraryMode_Click(LibraryListMode, new()); LibrarySearch.Text = "";
            result["libraryListModeRestores"] = LibraryList.Visibility == Visibility.Visible && LibraryGridMode.IsChecked == false && store.LoadSettings().LibraryViewMode == "List";
            Back_Click(this, new()); await Task.Delay(100);
            result["backReturnsToMediaWithoutClearingChat"] = WorkspaceView.Visibility == Visibility.Visible && current == fixture && ReferenceEquals(ChatMessages.ItemsSource, fixture.Chat);
            result["bundledTypographyConfigured"] = SessionTitle.FontFamily.Source.Contains("NotoSansSC") && File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "NotoSansSC.ttf"));
            BuildModelStore(); ShowPage(ModelStoreView);
            await Task.Delay(100); // Let the real WinUI layout establish the scroll extent.
            StoreScroll.ChangeView(null, StoreScroll.ScrollableHeight, null, true);
            ollamaButtons["test-summary-b"].Summary.Focus(FocusState.Programmatic);
            await Task.Delay(100);
            var scrollOffset = StoreScroll.VerticalOffset;
            var backdrop = SystemBackdrop; var rootChildren = Root.Children.ToArray(); var cards = StoreCards.Children.ToArray();
            var buttons = ollamaButtons.ToDictionary(p => p.Key, p => p.Value);
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 12; i++)
            {
                await UseOllamaModelAsync(i % 2 == 0 ? "test-summary-a" : "test-summary-b", false);
                await UseOllamaModelAsync(i % 2 == 0 ? "test-summary-a" : "test-translation", true);
                ApplySettings(); // Re-applying unchanged appearance must preserve the compositor backdrop.
                await Task.Yield();
            }
            await Task.Delay(100);
            result["modelSwitches"] = 24; result["milliseconds"] = watch.Elapsed.TotalMilliseconds;
            result["backdropIdentityPreserved"] = ReferenceEquals(backdrop, SystemBackdrop);
            result["windowTreePreserved"] = rootChildren.SequenceEqual(Root.Children);
            result["modelCardsPreserved"] = cards.SequenceEqual(StoreCards.Children);
            result["buttonsPreserved"] = buttons.All(p => ReferenceEquals(p.Value.Translate, ollamaButtons[p.Key].Translate) && ReferenceEquals(p.Value.Summary, ollamaButtons[p.Key].Summary));
            result["scrollOffsetBefore"] = scrollOffset; result["scrollOffsetAfter"] = StoreScroll.VerticalOffset;
            result["scrollPositionPreserved"] = scrollOffset > 0 && Math.Abs(scrollOffset - StoreScroll.VerticalOffset) < 1;
            result["focusPreserved"] = ollamaButtons["test-summary-b"].Summary.FocusState != FocusState.Unfocused;
            result["translationSaved"] = store.LoadSettings().TranslationModel == "test-translation";
            result["summarySaved"] = store.LoadSettings().SummaryModel == "test-summary-b";
            result["savedModel"] = store.LoadSettings().SummaryModel;
            var passed = result.Where(p => p.Value is bool).All(p => (bool)p.Value);
            result["passed"] = passed;
            if (!passed) throw new InvalidOperationException("UI identity regression");
            Status("UI 自测通过：24 次切换，背景、列表、滚动位置和焦点均保持稳定。");
        }
        catch (Exception ex) { result["passed"] = false; result["error"] = ex.ToString(); }
        await File.WriteAllTextAsync(Path.Combine(paths.DataRoot, "ui-test-results.json"), Json.Write(result));
        if (Environment.GetCommandLineArgs().Contains("--exit-after-test")) Close();
    }
}
