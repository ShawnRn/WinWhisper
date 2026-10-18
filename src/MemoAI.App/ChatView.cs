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
    private bool followChat = true, chatScrollPending;
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
        if (chatScroll is not null) return;
        T? Find<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T found) return found; if (Find<T>(child) is { } nested) return nested; }
            return null;
        }
        chatScroll = Find<ScrollViewer>(ChatMessages);
        if (chatScroll is not null) chatScroll.ViewChanged += (_, _) => { if (!chatScrollPending) followChat = chatScroll.ScrollableHeight - chatScroll.VerticalOffset < 50; };
        FollowChat();
    }
    private void FollowChat()
    {
        if (!followChat || chatScrollPending || ChatMessages.Visibility != Visibility.Visible) return;
        chatScrollPending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { if (!closing && chatScroll is not null) chatScroll.ChangeView(null, chatScroll.ScrollableHeight, null, true); chatScrollPending = false; });
    }
    private void CopyChat_Click(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.Tag is ChatTurn turn) { var data = new DataPackage(); data.SetText(turn.Content); Clipboard.SetContent(data); if (sender is Button button) button.Content = "已复制"; } }
    private void Question_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down) && AskAiButton.IsEnabled)
        { e.Handled = true; Ai_Click(AskAiButton, new()); }
    }
}
