using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private async void NewChat_Click(object sender, RoutedEventArgs e) => await SafeAsync(CreateNewChatAsync);
    private async Task CreateNewChatAsync()
    {
        RequireSession(); if (aiBusy || transcriptReviewCompletion is not null) return;
        var session = current!; ChatConversations.Ensure(session).Draft = QuestionBox.Text;
        ChatConversations.Create(session); ChatEditMode.IsChecked = false;
        store.SaveSession(session, recordVersion: false); LoadChat(session); UpdateAiAvailability();
        AiActivityText.Text = "新对话 · 仍可读取当前完整转写"; AiActivityDetails.Text = "";
        QuestionBox.Focus(FocusState.Programmatic); await Task.CompletedTask;
    }
    private void ChatHistory_Click(object sender, RoutedEventArgs e)
    {
        if (current is null || aiBusy || transcriptReviewCompletion is not null) return;
        var session = current; var active = ChatConversations.Ensure(session);
        var body = new StackPanel { Spacing = 10, Width = 310 };
        body.Children.Add(new TextBlock { Text = "当前媒体的历史对话", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var search = new TextBox { PlaceholderText = "搜索对话标题或消息" }; body.Children.Add(search);
        var list = new ListView { MaxHeight = 360, IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single, DisplayMemberPath = "Label" };
        void Filter()
        {
            var query = search.Text.Trim();
            list.ItemsSource = session.Conversations.Where(c => query.Length == 0 || c.Messages.Any(t => t.Content.Contains(query, StringComparison.OrdinalIgnoreCase)) || c.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.UpdatedAt).ToArray();
            list.SelectedItem = active;
        }
        Filter(); search.TextChanged += (_, _) => Filter(); body.Children.Add(list);
        var flyout = new Flyout { Content = body, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        list.ItemClick += async (_, args) => await SafeAsync(async () =>
        {
            if (args.ClickedItem is not ChatConversation target || current?.Id != session.Id || aiBusy || transcriptReviewCompletion is not null) return;
            active.Draft = QuestionBox.Text; ChatConversations.Switch(session, target.Id);
            store.SaveSession(session, recordVersion: false); LoadChat(session); UpdateAiAvailability();
            AiActivityText.Text = "历史对话 · 已恢复"; AiActivityDetails.Text = ""; flyout.Hide(); await Task.CompletedTask;
        });
        flyout.ShowAt((FrameworkElement)sender);
    }
}
