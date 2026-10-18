using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace MemoAI.Core;

public sealed class ChatConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string Draft { get; set; } = "";
    public ObservableCollection<ChatTurn> Messages { get; set; } = [];
    [JsonIgnore] public string Title
    {
        get
        {
            var first = Messages.FirstOrDefault(t => t.Role == "user")?.Content
                ?? Messages.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Content))?.Content ?? "新对话";
            first = first.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return first.Length > 36 ? first[..36] + "…" : first;
        }
    }
    [JsonIgnore] public string Label => $"{Title}\n{UpdatedAt:MM-dd HH:mm} · {Messages.Count} 条消息";
}

public static class ChatConversations
{
    public static ChatConversation Ensure(MediaSession session)
    {
        if (session.Conversations.Count == 0)
        {
            // Legacy Chat and Summary become one conversation, exactly once.
            if (session.Chat.Count == 0 && !string.IsNullOrWhiteSpace(session.Summary))
                session.Chat.Add(new() { Role = "assistant", Content = ChineseTypography.Apply(session.Summary), Status = "已保存的内容总结" });
            var legacy = new ChatConversation { Messages = session.Chat, CreatedAt = session.CreatedAt, UpdatedAt = session.UpdatedAt };
            session.Conversations.Add(legacy); session.ActiveConversationId = legacy.Id;
        }
        var active = session.Conversations.FirstOrDefault(c => c.Id == session.ActiveConversationId) ?? session.Conversations[0];
        session.ActiveConversationId = active.Id; session.Chat = active.Messages;
        return active;
    }
    public static ChatConversation Create(MediaSession session)
    {
        var active = Ensure(session);
        if (active.Messages.Count == 0 && string.IsNullOrWhiteSpace(active.Draft)) return active;
        var conversation = new ChatConversation(); session.Conversations.Add(conversation);
        Switch(session, conversation.Id); return conversation;
    }
    public static ChatConversation Switch(MediaSession session, string id)
    {
        Ensure(session);
        var conversation = session.Conversations.SingleOrDefault(c => c.Id == id) ?? throw new ArgumentException("对话不存在或属于其他媒体。");
        session.ActiveConversationId = conversation.Id; session.Chat = conversation.Messages;
        return conversation;
    }
}
