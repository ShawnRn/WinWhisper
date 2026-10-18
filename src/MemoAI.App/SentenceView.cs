using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private async void SegmentSentences_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSubtitles();
        if (aiBusy || transcriptReviewCompletion is not null || jobs.Any(j => j.SessionId == current!.Id && j.State is JobState.Running or JobState.Queued)) throw new InvalidOperationException("请先完成当前任务或提案审阅。");
        var session = current!; var before = session.Segments.Select(s => s.Copy()).ToArray(); var source = before.Select(s => s.Copy()).ToArray();
        var eligible = source.Where(s => SentenceSegmentation.IsEnglish(s.Text) && s.Translation.Length == 0).ToArray();
        if (eligible.Length == 0) throw new InvalidOperationException("没有可处理的英语原文。已有译文的字幕会保留边界，避免译文错位；如需重做，请先备份字幕。");
        var punctuation = new CheckBox { Content = "使用当前本地 AI 结合相邻字幕恢复英语标点（较慢）", IsChecked = true };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = $"可处理 {eligible.Length} 条英语字幕，其中 {eligible.Count(SentenceSegmentation.HasReliableWords)} 条有可用词时间戳。\n根据词时间戳与原音频停顿优化边界。没有词时间戳的字幕仅恢复标点、保留整段时间；可重新转写以获取词时间戳。已有译文的字幕保持原样。完成后在中间字幕区审阅，允许后写入。", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(punctuation);
        if (!await ConfirmAsync("优化英语断句", body, "优化")) return;
        if (current?.Id != session.Id || aiBusy) return;
        aiBusy = true; aiCancellation = new(); UpdateEditState(); BeginAiFeedback(null, "segment", punctuation.IsChecked == true ? settings.SummaryModel : "词时间戳 + 音频停顿");
        var token = aiCancellation.Token; var gateHeld = false;
        try
        {
            var pauses = File.Exists(session.MediaPath) ? await new MediaTools(paths).DetectSilencesAsync(session.MediaPath, token) : [];
            if (punctuation.IsChecked == true)
            {
                await AiService.GpuGate.WaitAsync(token); gateHeld = true;
                using var model = new LocalLanguageModelProvider(paths, Json.Read<AppSettings>(Json.Write(settings)));
                var restored = await TranscriptEditing.ProposeAsync(model, settings, eligible, "Restore English sentence punctuation and capitalization using the neighbouring transcript, without changing any words.", true,
                    (i, n) => { SetAiFeedback($"上下文标点恢复 · {i}/{n} 批", (i - 1d) / n); return Task.CompletedTask; }, token);
                var byId = restored.ToDictionary(s => s.Id);
                for (var i = 0; i < source.Length; i++) if (byId.TryGetValue(source[i].Id, out var restoredCue)) source[i] = restoredCue;
            }
            var optimized = await Task.Run(() => SentenceSegmentation.Optimize(source, pauses), token); token.ThrowIfCancellationRequested();
            if (gateHeld) { AiService.GpuGate.Release(); gateHeld = false; }
            var applied = await PreviewTranscriptAsync(session, before, optimized, "优化英语断句", "根据完整词序列选择边界，优先标点和停顿，避免句尾悬空冠词 / 介词。缺少词时间戳的条目保留时间范围。", token);
            SetAiFeedback(applied ? $"断句已应用 · {source.Length} → {optimized.Count} 条字幕" : "未应用断句，原字幕保留。", 1);
        }
        catch (OperationCanceledException) { SetAiFeedback("已停止断句，原字幕已保留。"); }
        finally { if (gateHeld) AiService.GpuGate.Release(); aiBusy = false; aiCancellation.Dispose(); aiCancellation = null; EndAiFeedback(); UpdateEditState(); }
    });
}
