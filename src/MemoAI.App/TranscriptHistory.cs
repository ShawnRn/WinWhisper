using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private async void TranscriptHistory_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSession();
        if (aiBusy || transcriptReviewCompletion is not null || jobs.Any(j => j.SessionId == current!.Id && j.State is JobState.Running or JobState.Queued)) throw new InvalidOperationException("请先完成当前任务或提案审阅。");
        var session = current!; await SaveCurrentAsync(); store.SaveSession(session);
        var list = new ListView { ItemsSource = store.GetTranscriptVersions(session.Id, metadataOnly: true), DisplayMemberPath = "Label", Height = 320, MinWidth = 380, SelectionMode = ListViewSelectionMode.Single, SelectedIndex = 0 };
        var body = new StackPanel { Spacing = 10 }; body.Children.Add(new TextBlock { Text = "待审阅提案可允许 / 拒绝；已应用版本可创建回滚提案。回滚追加新版本，不抹去后续历史。旧提案不能覆盖已经修改的字幕。", TextWrapping = TextWrapping.Wrap }); body.Children.Add(list);
        if (!await ConfirmAsync("字幕版本与提案", body, "查看所选版本") || list.SelectedItem is not TranscriptRevision selectedRevision || current?.Id != session.Id) return;
        var revision = store.GetTranscriptVersion(session.Id, selectedRevision.Id);
        if (revision.Status == "Pending") { await PreviewTranscriptAsync(session, revision.Before, revision.After, "审阅提案", revision.Instruction, revision: revision); return; }
        if (revision.Status == "Rejected") { await PreviewTranscriptAsync(session, revision.Before, revision.After, "已拒绝的提案", revision.Instruction + "\n此记录仅供查看，拒绝决定与当前字幕保持不变。", revision: revision, readOnly: true); return; }
        if (revision.Status is "Failed" or "Cancelled" or "Generating" or "NoChanges")
        { await MessageAsync("Agent 运行记录", revision.Label + "\n\n" + string.Join("\n", revision.Events.Select(ev => $"{ev.Sequence}. {ev.Tool} · {ev.State} · {ev.Message}"))); return; }
        var before = session.Segments.Select(c => c.Copy()).ToArray();
        if (TranscriptRevision.Hash(before) == TranscriptRevision.Hash(revision.After)) { Status("当前字幕已是此版本，无需回滚。"); return; }
        var proposal = store.CreateTranscriptProposal(session, before, revision.After, "回滚到版本 " + revision.Id[..8], kind: "Rollback");
        await PreviewTranscriptAsync(session, before, revision.After, "回滚字幕版本", $"目标版本 {revision.Id[..8]} · {revision.CreatedAt:yyyy-MM-dd HH:mm:ss}\n允许后生成新的历史记录；拒绝则保留当前版本。", revision: proposal);
    });
}
