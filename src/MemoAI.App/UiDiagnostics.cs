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
            var workArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
            result["initialWindowFitsDisplayWorkArea"] = AppWindow.Position.X >= workArea.X && AppWindow.Position.Y >= workArea.Y
                && AppWindow.Position.X + AppWindow.Size.Width <= workArea.X + workArea.Width && AppWindow.Position.Y + AppWindow.Size.Height <= workArea.Y + workArea.Height;
            ollamaModels = ["test-translation", "test-summary-a", ..Enumerable.Range(0, 12).Select(i => $"fixture-model-{i}"), "test-summary-b"];
            var navigationPassed = true;
            foreach (var (item, page) in new (NavigationViewItem, UIElement)[] { (NavQueue, QueueView), (NavRecord, RecordView), (NavLink, LinkView), (NavModels, ModelStoreView), (NavSettings, SettingsView), (NavLibrary, LibraryView) })
            {
                Sidebar.SelectedItem = item; await Task.Delay(40);
                navigationPassed &= item.IsSelected && page.Visibility == Visibility.Visible;
            }
            result["navigationTracksAllSixPages"] = navigationPassed;
            StoreCategory.SelectedIndex = 0; StoreSearch.Text = "parakeet"; BuildModelStore();
            result["parakeetModelsVisibleInStore"] = modelCards.Count == 3;
            StoreSearch.Text = "阿里"; BuildModelStore();
            result["alibabaModelsVisibleInStore"] = modelCards.Count == 6 && !modelCards.ContainsKey("funasr-nano-fp16");
            StoreSearch.Text = ""; StoreCategory.SelectedIndex = 0; BuildModelStore();
            var categoriesSeparated = modelCards.Count == 13 && modelCards.Keys.All(k => k.StartsWith("whisper-") || AsrModels.All.Any(m => m.Id == k)) && ollamaButtons.Count == 0;
            StoreCategory.SelectedIndex = 1; BuildModelStore();
            categoriesSeparated &= modelCards.Keys.SequenceEqual(new[] { "qwen" }) && ollamaButtons.Count == ollamaModels.Length && RefreshOllamaButton.Visibility == Visibility.Visible;
            StoreCategory.SelectedIndex = 2; BuildModelStore();
            categoriesSeparated &= modelCards.Keys.SequenceEqual(new[] { "kokoro" }) && ollamaButtons.Count == 0;
            StoreCategory.SelectedIndex = 3; BuildModelStore();
            categoriesSeparated &= modelCards.Keys.Order().SequenceEqual(new[] { "speakers", "vad" }) && RefreshOllamaButton.Visibility == Visibility.Collapsed;
            result["modelStoreSeparatesFourPurposes"] = categoriesSeparated;
            // Reproduce a preinstalled model at first render, then exercise the
            // enabled/disabled cycle on the actual generated native template.
            var installedFixture = Path.Combine(paths.ModelRoot, "installed-model-fixture"); File.WriteAllText(installedFixture, "fixture");
            settings.SpeakerSegmentationModel = settings.SpeakerEmbeddingModel = installedFixture;
            StoreInstalledOnly.IsChecked = true; BuildModelStore();
            result["downloadedFilterKeepsPurposeAndHidesUninstalledModels"] = StoreCategory.SelectedIndex == 3 && modelCards.Keys.SequenceEqual(new[] { "speakers" });
            StoreInstalledOnly.IsChecked = false;
            // Keep the tested card in the viewport: an offscreen ScrollViewer
            // child may have a template before its composition brush connects.
            StoreSearch.Text = "说话人"; BuildModelStore(); ShowPage(ModelStoreView); StoreScroll.ChangeView(null, 0, null, true); StoreCards.UpdateLayout(); await Task.Delay(100);
            var installedButton = modelCards["speakers"].Apply;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                StoreCards.UpdateLayout(); installedButton = modelCards["speakers"].Apply; installedButton.ApplyTemplate();
                if (installedButton.IsLoaded && installedButton.ActualWidth > 0) break;
                await Task.Delay(50);
            }
            var installedPaint = Descendants(installedButton).OfType<ContinuousSurface>().SingleOrDefault(s => s.Name == "G2_ContentPresenter");
            var keepsContour = !installedButton.IsEnabled && installedPaint?.HasGeometry == true && installedPaint.CornerRadius.TopLeft > 0;
            var paintIdentity = installedPaint?.PaintIdentity;
            for (var i = 0; i < 8; i++)
            {
                installedButton.IsEnabled = true; VisualStateManager.GoToState(installedButton, "PointerOver", false);
                installedButton.IsEnabled = false;
                keepsContour &= installedPaint?.HasGeometry == true && installedPaint.CornerRadius.TopLeft > 0 && ReferenceEquals(installedPaint.PaintIdentity, paintIdentity);
            }
            result["installedModelButtonKeepsG2ContourWhenDisabled"] = keepsContour;
            result["installedButtonSurface"] = $"enabled={installedButton.IsEnabled};loaded={installedPaint?.IsLoaded};size={installedPaint?.ActualWidth}x{installedPaint?.ActualHeight};radius={installedPaint?.CornerRadius};geometry={installedPaint?.HasGeometry};view={ModelStoreView.Visibility}/{ModelStoreView.IsLoaded};cards={StoreCards.IsLoaded}/{StoreCards.ActualHeight};current={ReferenceEquals(installedButton, modelCards["speakers"].Apply)}";
            StoreSearch.Text = ""; StoreCategory.SelectedIndex = 0; BuildModelStore();
            result["settingsContainAboutMetadata"] = Descendants(SettingsContent).OfType<TextBlock>().Any(t => t.Text.Contains("开发者：Shawn Rain") && t.Text.Contains("MIT"));
            result["settingsUseStoreInsteadOfDuplicateDownloadControls"] = Descendants(SettingsContent).OfType<Button>().Any(b => b.Content?.ToString() == "模型商店")
                && !Descendants(SettingsContent).OfType<Button>().Any(b => b.Content?.ToString()?.Contains("下载") == true);
            ShowPage(RecordView); await Task.Delay(80);
            var recordBounds = RecordingContent.TransformToVisual(RecordView).TransformBounds(new Windows.Foundation.Rect(0, 0, RecordingContent.ActualWidth, RecordingContent.ActualHeight));
            result["recordingColumnCentered"] = Math.Abs(recordBounds.X + recordBounds.Width / 2 - RecordView.ActualWidth / 2) < 3;
            result["recordingHasApplicationAndLiveOptions"] = RecordingMode.Items.Count == 4 && RecordingLive is not null && RecordingModel is not null;
            var navigationPresenter = Descendants(NavRecord).OfType<Microsoft.UI.Xaml.Controls.Primitives.NavigationViewItemPresenter>().FirstOrDefault();
            var hoverSurface = navigationPresenter is null ? null : Descendants(navigationPresenter).OfType<ContinuousSurface>().FirstOrDefault(s => s.Name == "G2_LayoutRoot");
            var restoredHover = hoverSurface is not null;
            if (hoverSurface is not null)
            {
                VisualStateManager.GoToState(navigationPresenter!, "Normal", false);
                var originalBackground = hoverSurface.Background; var originalCorner = hoverSurface.CornerRadius; var originalPaint = hoverSurface.PaintIdentity;
                for (var i = 0; i < 40; i++)
                {
                    VisualStateManager.GoToState(navigationPresenter!, "PointerOver", false);
                    restoredHover &= !ReferenceEquals(hoverSurface.Background, originalBackground) && hoverSurface.CornerRadius == originalCorner && hoverSurface.HasGeometry;
                    VisualStateManager.GoToState(navigationPresenter!, "Normal", false);
                    restoredHover &= ReferenceEquals(hoverSurface.Background, originalBackground) && ReferenceEquals(hoverSurface.PaintIdentity, originalPaint) && hoverSurface.HasGeometry;
                }
            }
            result["hoverRestoresImmediatelyWithStableGeometryAndBrush"] = restoredHover;
            var navBlankPoint = NavRecord.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(Math.Max(12, NavRecord.ActualWidth - 24), NavRecord.ActualHeight / 2));
            result["navigationBlankAreaIsHitTestable"] = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(navBlankPoint, Root).Contains(NavRecord);
            result["navigationDoesNotStartRecordingOrDownload"] = recorder is null && linkCancellation is null;
            result["bottomStatusBarRemoved"] = Root.RowDefinitions.Count == 2;
            var noticesFit = true;
            foreach (var width in new[] { 800, 1320, 1500 })
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 980)); await Task.Delay(100);
                foreach (var page in new UIElement[] { LibraryView, WorkspaceView })
                {
                    ShowPage(page); Status(string.Concat(Enumerable.Repeat("内容、录音及会话缓存已删除，外部原始媒体保留。", 8))); ShellLayout.UpdateLayout();
                    var noticeBounds = OperationNotice.TransformToVisual(ShellLayout).TransformBounds(new Windows.Foundation.Rect(0, 0, OperationNotice.ActualWidth, OperationNotice.ActualHeight));
                    var pageY = PageHost.TransformToVisual(ShellLayout).TransformPoint(new Windows.Foundation.Point()).Y;
                    noticesFit &= noticeBounds.Bottom <= pageY + 1 && OperationMessage.ActualHeight > 20 && DismissOperationNotice.ActualWidth >= 28;
                    DismissOperationNotice_Click(this, new()); ShellLayout.UpdateLayout();
                    noticesFit &= OperationNotice.Visibility == Visibility.Collapsed && PageHost.TransformToVisual(ShellLayout).TransformPoint(new Windows.Foundation.Point()).Y < 1;
                }
            }
            result["notificationsWrapAndReserveSpaceAcrossPagesAndWidths"] = noticesFit;
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
            var nextCue = new SubtitleSegment { Start = .35, End = 1, Text = "播放跟随" }; fixture.Segments[0].End = .35; fixture.Segments.Add(nextCue); RefreshSubtitles();
            ChatTimestamp_Click(this, TimeSpan.FromSeconds(.5)); await Task.Delay(80); PlaybackTick(this, new());
            result["timestampStartsPlayingAndSelectionFollowsWithoutSeekingBack"] = Player.MediaPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing && ReferenceEquals(SubtitleList.SelectedItem, nextCue) && Player.MediaPlayer.PlaybackSession.Position.TotalSeconds > .5;
            Player.MediaPlayer.Pause(); fixture.Segments.Remove(nextCue); fixture.Segments[0].End = 1; RefreshSubtitles();
            result["workspaceSelectsLibrary"] = NavLibrary.IsSelected;
            TranscriptionFlyout.ShowAt(TranscriptionSettingsButton); await Task.Delay(120);
            DependencyObject? flyoutParent = TranscriptionSettingsPanel;
            while (flyoutParent is not null && flyoutParent is not FlyoutPresenter) flyoutParent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(flyoutParent);
            result["transcriptionSettingsFitFlyout"] = flyoutParent is FlyoutPresenter presenter && presenter.ActualWidth >= TranscriptionSettingsPanel.ActualWidth;
            TranscriptionLanguage.IsDropDownOpen = true;
            Border[] popupBorders = [];
            for (var i = 0; i < 100; i++)
            {
                popupBorders = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).SelectMany(p => new[] { p.Child }.Concat(Descendants(p.Child))).OfType<Border>().Where(b => b.Name == "PopupBorder").ToArray();
                if (popupBorders.Any(b => b is { Child: ScrollViewer { Content: ItemsPresenter }, Background: ContinuousPopupBrush paint } && ReferenceEquals(paint.Owner, b))) break;
                await Task.Delay(20);
            }
            result["comboPopupDetails"] = string.Join(";", popupBorders.Select(b => $"{b.Child?.GetType().Name}/{(b.Child as ScrollViewer)?.Content?.GetType().Name}/{b.Background?.GetType().Name}/owner={ReferenceEquals((b.Background as ContinuousPopupBrush)?.Owner, b)}"));
            result["comboPopupPreservesNativeContentContract"] = popupBorders.Any(b => b is { Child: ScrollViewer { Content: ItemsPresenter }, Background: ContinuousPopupBrush paint } && ReferenceEquals(paint.Owner, b));
            TranscriptionBackend.IsDropDownOpen = true; await Task.Delay(200);
            result["transcriptionDropdownsCloseSiblings"] = TranscriptionBackend.IsDropDownOpen && !TranscriptionLanguage.IsDropDownOpen;
            CloseTranscriptionSettings_Click(this, new());
            // Native exit animations can take longer under x64 emulation. Await the
            // Closed state instead of assuming a fixed 250 ms animation duration.
            for (var i = 0; i < 100 && transcriptionFlyoutOpen; i++) await Task.Delay(20);
            result["settingsCloseDetails"] = $"open={transcriptionFlyoutOpen};checked={TranscriptionSettingsButton.IsChecked};dropdown={TranscriptionBackend.IsDropDownOpen}";
            result["settingsCloseResetsToggleAndChildDropdowns"] = !transcriptionFlyoutOpen && TranscriptionSettingsButton.IsChecked != true && !TranscriptionBackend.IsDropDownOpen;
            ChatEditMode.IsChecked = true;
            result["chatCanSwitchBetweenTranscriptQuestionsAndEdits"] = QuestionBox.PlaceholderText.Contains("断句") && TranscriptAccessLabel.Text.Contains("全文");
            ChatEditMode.IsChecked = false;
            result["fillerCleanupIsConfiguredInFrozenTranscriptionOptions"] = TranscriptionFillers.IsChecked == true && ReadTranscriptionOptions().RemoveEnglishFillers;
            var editBefore = fixture.Segments.Select(c => c.Copy()).ToArray();
            var editAfter = fixture.Segments.Select(c => { var copy = c.Copy(); copy.Text = "Edited preview."; return copy; }).ToArray();
            var editPreview = CreateTranscriptPreview(editBefore, editAfter, "fixture", 0);
            var wordDiffView = new TranscriptDiffView { Diff = new TranscriptDiff("0–2", "修改", "we are at spacex", "we are at SpaceX") };
            var diffRows = ((StackPanel)wordDiffView.Content).Children.OfType<RichTextBlock>().ToArray();
            var afterRuns = diffRows[1].Blocks.OfType<Microsoft.UI.Xaml.Documents.Paragraph>().Single().Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().ToArray();
            result["wordDiffKeepsUnchangedContextInNormalColor"] = afterRuns.Any(r => r.Text == "we are at " && r.ReadLocalValue(Microsoft.UI.Xaml.Documents.TextElement.ForegroundProperty) == DependencyProperty.UnsetValue) && afterRuns.Any(r => r.Text == "SpaceX" && r.ReadLocalValue(Microsoft.UI.Xaml.Documents.TextElement.ForegroundProperty) != DependencyProperty.UnsetValue);
            var translationBefore = new SubtitleSegment { Id = "translation-diff", Text = "Original transcript.", Start = 1, End = 2 };
            var translationAfter = translationBefore.Copy(); translationAfter.Translation = "新增译文";
            var translationView = new TranscriptDiffView { Diff = TranscriptDiff.Build([translationBefore], [translationAfter]).Single() };
            var translationRows = ((StackPanel)translationView.Content).Children;
            result["translationDiffShowsOriginalOnceAndOnlyComparesTranslation"] = translationRows.OfType<TextBlock>().Single().Text == translationBefore.Text && translationRows.OfType<RichTextBlock>().Count() == 1 && string.Concat(translationRows.OfType<RichTextBlock>().Single().Blocks.OfType<Microsoft.UI.Xaml.Documents.Paragraph>().Single().Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().Select(r => r.Text)) == "+ 新增译文";
            result["transcriptPreviewShowsCompleteBeforeAndAfterWithoutApplying"] = editPreview.Children.OfType<Pivot>().Single().Items.Count == 3 && fixture.Segments[0].Text == editBefore[0].Text;
            var inlineRevision = store.CreateTranscriptProposal(fixture, editBefore, editAfter, "inline fixture");
            var inlineReview = PreviewTranscriptAsync(fixture, editBefore, editAfter, "inline fixture", "fixture", revision: inlineRevision);
            await Task.Delay(50); TranscriptReviewHost.UpdateLayout();
            result["transcriptReviewOccupiesOriginalPaneWithoutModalOrEarlyWrite"] = TranscriptReviewHost.Visibility == Visibility.Visible && TranscriptReviewHost.ActualWidth > 0 && SubtitleList.Visibility == Visibility.Collapsed && !SubtitleEditorPane.IsEnabled && fixture.Segments[0].Text == editBefore[0].Text;
            CompleteTranscriptReview(); await inlineReview;
            result["laterKeepsPersistentProposalAndRestoresTranscriptPane"] = store.GetTranscriptVersion(fixture.Id, inlineRevision.Id).Status == "Pending" && TranscriptReviewHost.Visibility == Visibility.Collapsed && SubtitleList.Visibility == Visibility.Visible;
            inlineReview = PreviewTranscriptAsync(fixture, editBefore, editAfter, "inline fixture", "fixture", revision: inlineRevision);
            CompleteTranscriptReview(ContentDialogResult.Secondary); await inlineReview;
            result["inlineRejectRetainsOriginalTranscript"] = store.GetTranscriptVersion(fixture.Id, inlineRevision.Id).Status == "Rejected" && fixture.Segments[0].Text == editBefore[0].Text;
            inlineReview = PreviewTranscriptAsync(fixture, editBefore, editAfter, "rejected fixture", "fixture", revision: store.GetTranscriptVersion(fixture.Id, inlineRevision.Id), readOnly: true);
            result["rejectedHistoryIsReadOnly"] = Descendants(TranscriptReviewHost).OfType<Button>().Where(b => b.Content as string is "允许修改" or "拒绝修改").All(b => b.Visibility == Visibility.Collapsed);
            CompleteTranscriptReview(); await inlineReview;
            inlineReview = PreviewTranscriptAsync(fixture, editBefore, editAfter, "inline fixture", "fixture");
            CompleteTranscriptReview(ContentDialogResult.Primary); await inlineReview; Undo_Click(this, new());
            result["transcriptEditsAreUndoableAndRejectStalePreviews"] = fixture.Segments[0].Text == editBefore[0].Text;
            fixture.Segments[0].Text = "Changed after preview";
            try { CommitTranscriptChanges(fixture, editBefore, editAfter); result["transcriptEditsAreUndoableAndRejectStalePreviews"] = false; } catch (InvalidOperationException) { }
            fixture.Segments[0].Text = editBefore[0].Text; RefreshSubtitles();
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
            // Replay cumulative Worker snapshots while a cue is selected and the
            // transcript is scrolled. The old ItemsSource reset recreated these
            // containers and lost selection on every batch.
            var originalCues = fixture.Segments.ToArray();
            fixture.Segments = new(Enumerable.Range(0, 80).Select(i => new SubtitleSegment { Start = i * 2, End = i * 2 + 1.8, Text = $"It’s subtitle {i}. SpaceX’s rocket." }));
            TrackSegments(); RefreshSubtitles(); sessions.Add(fixture);
            var watchedCue = fixture.Segments[20];
            followingPlaybackSelection = true; SubtitleList.SelectedItem = watchedCue; followingPlaybackSelection = false;
            SubtitleList.ScrollIntoView(watchedCue); await Task.Delay(100); SubtitleList.UpdateLayout();
            var watchedContainer = SubtitleList.ContainerFromItem(watchedCue);
            var stableSource = SubtitleList.ItemsSource; var stableCues = fixture.Segments;
            var stableChat = ChatMessages.ItemsSource; var stableMedia = Player.Source; var stableBackdrop = SystemBackdrop;
            var transcriptScroll = Descendants(SubtitleList).OfType<ScrollViewer>().First();
            var scrollBefore = transcriptScroll.VerticalOffset; var navBefore = navigationAnimationCount;
            var anchorBefore = (watchedContainer as FrameworkElement)?.TransformToVisual(SubtitleList).TransformPoint(new Windows.Foundation.Point()).Y;
            var resets = 0; var unchangedNotifications = 0;
            System.Collections.Specialized.NotifyCollectionChangedEventHandler countResets = (_, args) => { if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++; };
            System.ComponentModel.PropertyChangedEventHandler countChanges = (_, _) => unchangedNotifications++;
            visibleSegments.CollectionChanged += countResets; watchedCue.PropertyChanged += countChanges;
            for (var batch = 0; batch < 8; batch++)
            {
                var snapshot = fixture.Segments.Select(c => c.Copy()).Append(new() { Start = 160 + batch * 2, End = 161.8 + batch * 2, Text = $"New batch {batch}." }).ToArray();
                await NotifyJobAsync(job, new("segments", "字幕已保存到检查点", .42, snapshot));
                await Task.Delay(20);
            }
            SubtitleList.UpdateLayout();
            var anchorAfter = (SubtitleList.ContainerFromItem(watchedCue) as FrameworkElement)?.TransformToVisual(SubtitleList).TransformPoint(new Windows.Foundation.Point()).Y;
            // VirtualizingStackPanel adjusts its estimated extent as new items
            // arrive. Offset can change while the same visible anchor stays in
            // exactly the same place; assert the user's reading position.
            result["streamingSubtitlesKeepSourceContainersSelectionAndScroll"] = watchedContainer is not null && ReferenceEquals(stableSource, SubtitleList.ItemsSource)
                && ReferenceEquals(stableCues, fixture.Segments) && ReferenceEquals(watchedCue, SubtitleList.SelectedItem)
                && ReferenceEquals(watchedContainer, SubtitleList.ContainerFromItem(watchedCue)) && anchorBefore is >= 0 && anchorBefore < SubtitleList.ActualHeight
                && anchorAfter.HasValue && Math.Abs(anchorBefore.Value - anchorAfter.Value) < 1 && resets == 0 && unchangedNotifications == 0;
            result["streamingSubtitleDetails"] = $"realized={watchedContainer is not null};source={ReferenceEquals(stableSource, SubtitleList.ItemsSource)};collection={ReferenceEquals(stableCues, fixture.Segments)};selection={ReferenceEquals(watchedCue, SubtitleList.SelectedItem)};container={ReferenceEquals(watchedContainer, SubtitleList.ContainerFromItem(watchedCue))};offset={scrollBefore}/{transcriptScroll.VerticalOffset};anchor={anchorBefore}/{(SubtitleList.ContainerFromItem(watchedCue) as FrameworkElement)?.TransformToVisual(SubtitleList).TransformPoint(new Windows.Foundation.Point()).Y};resets={resets};notifications={unchangedNotifications}";
            result["streamingSubtitlesDoNotRebuildWorkspace"] = ReferenceEquals(stableChat, ChatMessages.ItemsSource) && ReferenceEquals(stableMedia, Player.Source) && ReferenceEquals(stableBackdrop, SystemBackdrop) && navBefore == navigationAnimationCount;
            var corrected = fixture.Segments.Select(c => c.Copy()).ToArray(); corrected[20].Text = "It’s corrected in place.";
            await NotifyJobAsync(job, new("segments", "修正一条字幕", .43, corrected));
            result["streamingSubtitleCorrectionNotifiesOnlyChangedCue"] = watchedCue.Text == corrected[20].Text && unchangedNotifications == 1 && ReferenceEquals(watchedContainer, SubtitleList.ContainerFromItem(watchedCue));
            visibleSegments.CollectionChanged -= countResets; watchedCue.PropertyChanged -= countChanges;
            sessions.Remove(fixture); fixture.Segments = new(originalCues); TrackSegments(); RefreshSubtitles();
            var quoteProbe = new TextBlock { Text = "’", FontSize = 16, FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["ContentControlThemeFontFamily"] };
            quoteProbe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            result["englishApostropheUsesProportionalGlyph"] = quoteProbe.DesiredSize.Width > 0 && quoteProbe.DesiredSize.Width < 8;
            result["englishApostropheWidth"] = quoteProbe.DesiredSize.Width;
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
            LibraryGrid.UpdateLayout(); await Task.Delay(100);
            var gridItem = LibraryGrid.ContainerFromItem(fixture) as GridViewItem;
            IEnumerable<DependencyObject> Descendants(DependencyObject node)
            {
                for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i); yield return child;
                    foreach (var descendant in Descendants(child)) yield return descendant;
                }
            }
            var gridTree = gridItem is null ? [] : Descendants(gridItem).ToArray();
            var gridSurfaces = gridTree.OfType<ContinuousSurface>().Where(b => b.Name == "SingleSurface").ToArray();
            result["gridSurfaceDiagnostics"] = $"container={gridItem?.GetType().Name}; surfaces={gridSurfaces.Length}; state={string.Join(',', gridSurfaces.Select(s => $"{s.IsLoaded}/{s.ActualWidth}x{s.ActualHeight}/{s.Background?.GetType().Name}/{s.HasGeometry}"))}; tree={string.Join(',', gridTree.Take(12).Select(n => n.GetType().Name))}";
            // A virtualized container can already be measured while its surface is unloaded.
            // Check selection layering here; live surface painting is checked below on the workspace.
            result["gridUsesSingleStateSurface"] = gridSurfaces.Length == 1 && !gridTree.OfType<Microsoft.UI.Xaml.Controls.Primitives.GridViewItemPresenter>().Any();
            var longTitle = new MediaSession { Title = "这是一段用于验证两行标题与短标题在同一行保持封面尺寸和元数据基线一致的很长的标题" };
            sessions.Add(longTitle); RefreshLibrary(); LibraryGrid.UpdateLayout(); await Task.Delay(100);
            gridItem = LibraryGrid.ContainerFromItem(fixture) as GridViewItem;
            var secondItem = LibraryGrid.ContainerFromItem(longTitle) as GridViewItem;
            var firstCover = gridItem is null ? null : Descendants(gridItem).OfType<MediaThumbnail>().FirstOrDefault();
            var secondCover = secondItem is null ? null : Descendants(secondItem).OfType<MediaThumbnail>().FirstOrDefault();
            var firstMeta = gridItem is null ? null : Descendants(gridItem).OfType<TextBlock>().FirstOrDefault(t => t.Name == "GridMediaMeta");
            var secondMeta = secondItem is null ? null : Descendants(secondItem).OfType<TextBlock>().FirstOrDefault(t => t.Name == "GridMediaMeta");
            result["libraryCardsAlignShortAndLongTitlesWithSameAspect"] = firstCover is not null && secondCover is not null && firstMeta is not null && secondMeta is not null
                && Math.Abs(firstCover.ActualWidth / firstCover.ActualHeight - 16d / 9) < .01 && Math.Abs(firstCover.ActualHeight - secondCover.ActualHeight) < 1
                && Math.Abs(firstCover.TransformToVisual(LibraryGrid).TransformPoint(new()).Y - secondCover.TransformToVisual(LibraryGrid).TransformPoint(new()).Y) < 1
                && Math.Abs(firstMeta.TransformToVisual(LibraryGrid).TransformPoint(new()).Y - secondMeta.TransformToVisual(LibraryGrid).TransformPoint(new()).Y) < 1;
            result["libraryCardAlignmentDetails"] = $"first={firstCover?.ActualWidth}x{firstCover?.ActualHeight}/{firstMeta?.ActualHeight};second={secondCover?.ActualWidth}x{secondCover?.ActualHeight}/{secondMeta?.ActualHeight}";
            sessions.Remove(longTitle); RefreshLibrary();
            result["recentHostDoesNotDrawOuterSelectionSurface"] = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(RecentHost, 0) is ContentPresenter;
            result["libraryGridPersistsModeAndSelection"] = LibraryGrid.Visibility == Visibility.Visible && LibraryGrid.SelectedItems.Contains(fixture) && store.LoadSettings().LibraryViewMode == "Grid";
            LibrarySearch.Text = fixture.Title;
            result["libraryViewsShareFilters"] = ReferenceEquals(LibraryGrid.ItemsSource, LibraryList.ItemsSource) && LibraryGrid.Items.Count == 1;
            LibraryMode_Click(LibraryListMode, new()); LibrarySearch.Text = "";
            result["libraryListModeRestores"] = LibraryList.Visibility == Visibility.Visible && LibraryGridMode.IsChecked == false && store.LoadSettings().LibraryViewMode == "List";
            Back_Click(this, new()); await Task.Delay(100);
            result["backReturnsToMediaWithoutClearingChat"] = WorkspaceView.Visibility == Visibility.Visible && current == fixture && ReferenceEquals(ChatMessages.ItemsSource, fixture.Chat);
            Forward_Click(this, new()); await Task.Delay(60);
            result["forwardRestoresLibrary"] = LibraryView.Visibility == Visibility.Visible && NavigationBack.IsEnabled;
            Back_Click(this, new()); await Task.Delay(60);
            var animationCount = navigationAnimationCount;
            ShowPage(WorkspaceView);
            result["samePageDoesNotReplayAnimation"] = navigationAnimationCount == animationCount;
            var reduceMotion = settings.ReduceMotion; settings.ReduceMotion = true;
            ShowPage(QueueView); ShowPage(WorkspaceView);
            result["reduceMotionSkipsPageAnimations"] = navigationAnimationCount == animationCount;
            settings.ReduceMotion = false;
            for (var i = 0; i < 12; i++) { ShowPage(QueueView); ShowPage(WorkspaceView); }
            await Task.Delay(260);
            var pageVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(WorkspaceView);
            pageVisual.Properties.TryGetVector3("Translation", out var finalTranslation);
            result["rapidNavigationSettlesWithoutOffsetOrFading"] = WorkspaceView.Visibility == Visibility.Visible && QueueView.Visibility == Visibility.Collapsed && pageVisual.Opacity == 1 && finalTranslation.Length() < .01;
            result["pageMotionRespectsSystemPreference"] = motionSettings.AnimationsEnabled ? navigationAnimationCount >= animationCount + 24 : navigationAnimationCount == animationCount;
            settings.ReduceMotion = reduceMotion;
            result["bundledTypographyConfigured"] = SessionTitle.FontFamily.Source.Contains("NotoSansSC") && File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "NotoSansSC.ttf"));

            result["continuousCornersAppliedWithoutErrors"] = TranscriptionProgressCard.HasGeometry;
            var controls = ContinuousCorners.CornerControls(100, 12);
            var startTangent = 3 * (controls[1] - controls[0]); var endTangent = 3 * (controls[6] - controls[5]);
            var startSecond = 6 * (controls[2] - 2 * controls[1] + controls[0]); var endSecond = 6 * (controls[6] - 2 * controls[5] + controls[4]);
            float Cross(System.Numerics.Vector2 a, System.Numerics.Vector2 b) => a.X * b.Y - a.Y * b.X;
            result["g2CornersHaveEdgeTangentsAndZeroEndpointCurvature"] = startTangent.X > 0 && startTangent.Y == 0 && endTangent.Y > 0 && endTangent.X == 0 && Cross(startTangent, startSecond) == 0 && Cross(endTangent, endSecond) == 0;
            var rounded = ContinuousCorners.CornerControls(100, ContinuousCorners.CornerExtent(18, 31));
            result["continuousCornersKeepExpectedDiagonalRoundness"] = Math.Abs(100 - rounded[3].X - 18 * (1 - Math.Sqrt(.5))) < .001;
            var midLeft = 3 * (controls[3] - controls[2]); var midRight = 3 * (controls[4] - controls[3]);
            var leftSecond = 6 * (controls[3] - 2 * controls[2] + controls[1]); var rightSecond = 6 * (controls[5] - 2 * controls[4] + controls[3]);
            result["continuousCornersKeepCurvatureAcrossBothArcs"] = (midLeft - midRight).Length() < .001 && Math.Abs(Cross(midLeft, leftSecond) - Cross(midRight, rightSecond)) < .01;
            var originalTheme = Root.RequestedTheme;
            Root.RequestedTheme = ElementTheme.Light; await Task.Delay(100);
            var lightBrush = TranscriptionProgressCard.Background as Microsoft.UI.Xaml.Media.SolidColorBrush;
            var lightColor = lightBrush?.Color;
            var lightShell = (StableBackground.Background as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color;
            Root.RequestedTheme = ElementTheme.Dark; await Task.Delay(100);
            var darkBrush = TranscriptionProgressCard.Background as Microsoft.UI.Xaml.Media.SolidColorBrush;
            result["continuousSurfaceTracksTheme"] = lightColor.HasValue && darkBrush is not null && lightColor.Value != darkBrush.Color;
            var darkShell = (StableBackground.Background as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color;
            result["shellSurfaceTracksTheme"] = lightShell.HasValue && darkShell.HasValue && lightShell != darkShell;
            Root.RequestedTheme = originalTheme; await Task.Delay(100);
            var idleGeometry = TranscriptionProgressCard.GeometryUpdates; var idleConnections = TranscriptionProgressCard.Connections; var idlePaint = TranscriptionProgressCard.PaintIdentity;
            await Task.Delay(800); // Includes several playback timer ticks with no user action.
            result["idleBackgroundDoesNotRebuildGeometryOrReconnect"] = TranscriptionProgressCard.GeometryUpdates == idleGeometry && TranscriptionProgressCard.Connections == idleConnections && ReferenceEquals(idlePaint, TranscriptionProgressCard.PaintIdentity);
            result["defaultBackgroundIsNativeSolidWithoutOuterCompositionSurface"] = SystemBackdrop is null && StableBackground.Visibility == Visibility.Visible && ShellSurface.Background is null;
            StoreCategory.SelectedIndex = 1; BuildModelStore(); ShowPage(ModelStoreView);
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
            if (Environment.GetCommandLineArgs().Contains("--ui-test-recording"))
            {
                var tone = Path.Combine(paths.DataRoot, "own-process-tone.wav");
                using (var wave = new BinaryWriter(File.Create(tone)))
                {
                    wave.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); wave.Write(16000 * 2 * 5 + 36); wave.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                    wave.Write(16); wave.Write((short)1); wave.Write((short)1); wave.Write(16000); wave.Write(32000); wave.Write((short)2); wave.Write((short)16);
                    wave.Write(System.Text.Encoding.ASCII.GetBytes("data")); wave.Write(16000 * 2 * 5);
                    for (var n = 0; n < 16000 * 5; n++) wave.Write((short)(Math.Sin(n * 2 * Math.PI * 440 / 16000) * 12000));
                }
                using var capture = new AudioRecorder(paths.DataRoot, 3, paths, (uint)Environment.ProcessId);
                await capture.StartAsync();
                fixture.MediaPath = tone; fixture.PlaybackPath = ""; await OpenSessionAsync(fixture);
                for (var i = 0; i < 100 && ownedPlayer.PlaybackSession.NaturalDuration.TotalSeconds < 4; i++) await Task.Delay(20);
                ownedPlayer.Volume = .03; ownedPlayer.Play(); await Task.Delay(1800); ownedPlayer.Pause();
                var clip = Path.Combine(paths.DataRoot, "own-process-clip.wav");
                await Task.Run(() => capture.SnapshotAsync(clip, 0, 1.5, CancellationToken.None));
                var samples = SherpaTranscription.ReadPcm16(clip);
                result["singleApplicationLoopbackCapturesOwnAudio"] = samples.Length == 24000 && samples.Any(s => Math.Abs(s) > .0001f);
                var recording = await capture.StopAsync();
                result["recordingSnapshotAndFinalWaveReopen"] = await new MediaTools(paths).ProbeAsync(recording, CancellationToken.None) >= 1.5;
            }
            var playbackVideo = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--ui-test-video="))?[16..];
            if (playbackVideo is not null)
            {
                fixture.MediaPath = playbackVideo; fixture.PlaybackPath = ""; await OpenSessionAsync(fixture);
                for (var i = 0; i < 200 && Player.MediaPlayer.PlaybackSession.NaturalVideoHeight == 0; i++) await Task.Delay(25);
                result["realVideoLoadedBeforeShutdown"] = Player.MediaPlayer.PlaybackSession.NaturalVideoHeight > 0;
            }
            var passed = result.Where(p => p.Value is bool).All(p => (bool)p.Value);
            result["passed"] = passed;
            if (!passed) throw new InvalidOperationException("UI identity regression");
            Status("UI 自测通过：24 次切换，背景、列表、滚动位置和焦点均保持稳定。");
        }
        catch (Exception ex) { result["passed"] = false; result["error"] = ex.ToString(); }
        await File.WriteAllTextAsync(Path.Combine(paths.DataRoot, "ui-test-results.json"), Json.Write(result));
        if (Environment.GetCommandLineArgs().Contains("--exit-after-test")) await ShutdownAndCloseAsync();
    }
}
