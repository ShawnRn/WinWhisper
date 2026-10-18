using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private ScrollViewer? chatScroll;
    private bool followChat = true, chatScrollPending, questionComposing;
    private double chatLastOffset, chatLastExtent;
    private void LoadChat(MediaSession session)
    {
        if (session.Chat.Count == 0 && !string.IsNullOrWhiteSpace(session.Summary))
            session.Chat.Add(new() { Role = "assistant", Content = ChineseTypography.Apply(session.Summary), Status = "已保存的内容总结" });
        foreach (var turn in session.Chat.Where(t => t.Role == "assistant")) turn.Content = ChineseTypography.Apply(turn.Content);
        ChatMessages.ItemsSource = session.Chat; followChat = true; ShowChat(); FollowChat();
    }
    private ChatTurn BeginChatResponse(MediaSession session, string operation, string question)
    {
        session.Chat.Add(new() { Role = "user", Content = operation == "ask" ? question : operation switch { "summary" => "请总结这段内容。", "translate" => "请翻译字幕。", "chapters" => "请按时间生成章节目录。", "mindmap" => "请生成思维导图。", _ => "请校对字幕。" } });
        var response = new ChatTurn { Role = "assistant", Content = "", Status = "等待本地模型…" }; session.Chat.Add(response);
        if (operation == "ask") QuestionBox.Text = "";
        followChat = true; FollowChat(); return response;
    }
    private void ShowChat() { ChatMessages.Visibility = Visibility.Visible; MindMapTree.Visibility = BackToChat.Visibility = Visibility.Collapsed; }
    private void BackToChat_Click(object sender, RoutedEventArgs e) => ShowChat();
    private void UpdateAiModelLabel()
    {
        AiProviderLabel.Text = settings.ModelProvider == LanguageModelKind.BuiltIn ? Path.GetFileNameWithoutExtension(settings.GgufPath) : settings.SummaryModel.Split('/').Last();
        ToolTipService.SetToolTip(AiProviderLabel, "当前本地对话模型，点击打开模型商店切换。");
    }
    private void ChatBubble_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ChatTurn turn)
        { border.Background = turn.Role == "user" ? (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"] : null; border.Margin = new Thickness(turn.Role == "user" ? 20 : 0, 0, 0, 0); }
    }
    private void ChatMessages_Loaded(object sender, RoutedEventArgs e)
    {
        AttachChatScroll();
        ChatMessages.LayoutUpdated -= ChatLayoutUpdated;
        ChatMessages.LayoutUpdated += ChatLayoutUpdated;
        FollowChat();
    }
    private void ChatLayoutUpdated(object? sender, object e) => FollowChat();
    private void AttachChatScroll()
    {
        if (chatScroll is not null) return;
        ChatMessages.ApplyTemplate();
        T? Find<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T found) return found; if (Find<T>(child) is { } nested) return nested; }
            return null;
        }
        chatScroll = Find<ScrollViewer>(ChatMessages);
        if (chatScroll is not null) chatScroll.ViewChanged += (_, _) =>
        {
            var offset = chatScroll.VerticalOffset; var extent = chatScroll.ScrollableHeight;
            // New streamed content changes the extent; that is not the user scrolling away.
            if (!chatScrollPending && offset < chatLastOffset - 1 && Math.Abs(extent - chatLastExtent) < 1) followChat = false;
            if (extent - offset < 8) followChat = true;
            chatLastOffset = offset; chatLastExtent = extent;
            FollowChat();
        };
    }
    private void FollowChat()
    {
        if (chatScroll is null && ChatMessages.IsLoaded) AttachChatScroll();
        if (!followChat || chatScrollPending || chatScroll is null || ChatMessages.Visibility != Visibility.Visible || chatScroll.ScrollableHeight - chatScroll.VerticalOffset < 1) return;
        chatScrollPending = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try { if (!closing && followChat && chatScroll is not null) chatScroll.ChangeView(null, chatScroll.ScrollableHeight, null, true); }
            finally { chatScrollPending = false; }
        })) chatScrollPending = false;
    }
    private void ChatTimestamp_Click(object? sender, TimeSpan position)
    {
        if (current is null || Player.Source is null || Player.MediaPlayer is null) { Status("请先关联可播放的音视频文件。"); return; }
        var duration = Player.MediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds;
        var seconds = Math.Clamp(position.TotalSeconds, 0, Math.Max(0, duration));
        var cue = current.Segments.FirstOrDefault(s => s.Start <= seconds && s.End > seconds);
        if (cue is not null && SubtitleList.ItemsSource is IEnumerable<SubtitleSegment> displayed && displayed.Contains(cue)) { SubtitleList.SelectedItem = cue; SubtitleList.ScrollIntoView(cue); }
        Player.MediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(seconds);
        RefreshPlaybackControls();
    }
    private void CopyChat_Click(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.Tag is ChatTurn turn) { var data = new DataPackage(); data.SetText(turn.Content); Clipboard.SetContent(data); if (sender is Button button) button.Content = "已复制"; } }
    private void Question_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || questionComposing || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down)) return;
        e.Handled = true;
        if (AskAiButton.IsEnabled && !e.KeyStatus.WasKeyDown) Ai_Click(AskAiButton, new());
    }
}
