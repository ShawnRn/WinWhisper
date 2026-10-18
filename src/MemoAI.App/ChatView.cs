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
    private bool followChat = true, chatScrollPending, questionComposing, loadingConversation;
    private double chatLastOffset, chatLastExtent;
    private void LoadChat(MediaSession session)
    {
        var conversation = ChatConversations.Ensure(session);
        foreach (var turn in session.Chat.Where(t => t.Role == "assistant")) turn.Content = ChineseTypography.Apply(turn.Content);
        ChatMessages.ItemsSource = session.Chat; followChat = true; ShowChat(); FollowChat();
        loadingConversation = true;
        try { QuestionBox.Text = conversation.Draft; } finally { loadingConversation = false; }
        ToolTipService.SetToolTip(ChatHistoryButton, "历史对话 · " + conversation.Title);
    }
    private ChatTurn BeginChatResponse(MediaSession session, string operation, string question)
    {
        session.Chat.Add(new() { Role = "user", Content = operation == "ask" ? question : operation switch { "summary" => "请总结这段内容。", "translate" => "请翻译字幕。", "chapters" => "请按时间生成章节目录。", "mindmap" => "请生成思维导图。", _ => "请校对字幕。" } });
        var response = new ChatTurn { Role = "assistant", Content = "", Status = "等待本地模型…" }; session.Chat.Add(response);
        ChatConversations.Ensure(session).UpdatedAt = DateTimeOffset.Now;
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
        if (sender is ContinuousSurface border && border.DataContext is ChatTurn turn)
        { border.Margin = new Thickness(turn.Role == "user" ? 20 : 0, 0, 0, 0); }
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
        Player.MediaPlayer.Play();
        RefreshPlaybackControls();
    }
    private void CopyChat_Click(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.Tag is ChatTurn turn) { var data = new DataPackage(); data.SetText(turn.Content); Clipboard.SetContent(data); if (sender is Button { Content: FontIcon icon } button) { icon.Glyph = "\uE73E"; ToolTipService.SetToolTip(button, "已复制"); } } }
    private void ChatChrome_Entered(object sender, PointerRoutedEventArgs e) => SetChatCopyVisibility((Grid)sender, true);
    private void ChatChrome_Exited(object sender, PointerRoutedEventArgs e)
    {
        var grid = (Grid)sender; var point = e.GetCurrentPoint(grid).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < grid.ActualWidth && point.Y < grid.ActualHeight) return;
        SetChatCopyVisibility(grid, Within(FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject, grid));
    }
    private void ChatChrome_Focus(object sender, RoutedEventArgs e)
    {
        var grid = (Grid)sender;
        DispatcherQueue.TryEnqueue(() => SetChatCopyVisibility(grid, Within(FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject, grid)));
    }
    private void SetChatCopyVisibility(Grid grid, bool visible)
    {
        if (grid.FindName("CopyTurnButton") is not Button button) return;
        button.Opacity = visible ? 1 : 0; button.IsHitTestVisible = visible;
        if (!visible && button.Content is FontIcon icon) { icon.Glyph = "\uE8C8"; ToolTipService.SetToolTip(button, "复制消息"); }
    }
    private void Question_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || questionComposing || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down)) return;
        e.Handled = true;
        if (AskAiButton.IsEnabled && !e.KeyStatus.WasKeyDown) Ai_Click(AskAiButton, new());
    }
}
