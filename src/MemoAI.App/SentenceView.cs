using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private async void SegmentSentences_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        RequireSubtitles();
        if (aiBusy || jobs.Any(j => j.SessionId == current!.Id && j.State is JobState.Running or JobState.Queued)) throw new InvalidOperationException("请等待当前任务完成后优化断句。");
        var session = current!; var source = session.Segments.Select(s => s.Copy()).ToArray();
        var eligible = source.Where(s => SentenceSegmentation.IsEnglish(s.Text) && s.Translation.Length == 0).ToArray();
        if (eligible.Length == 0) throw new InvalidOperationException("没有可处理的英语原文。已有译文的字幕会保留边界，避免译文错位；如需重做，请先备份字幕。");
        var punctuation = new CheckBox { Content = "使用当前本地 AI 恢复英语标点（较慢）", IsChecked = false };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = $"可处理 {eligible.Length} 条英语字幕，其中 {eligible.Count(SentenceSegmentation.HasReliableWords)} 条有可用词时间戳。\n根据词时间戳与原音频停顿优化边界。没有词时间戳的字幕仅恢复标点、保留整段时间；可重新转写以获取词时间戳。已有译文的字幕保持原样。处理完成后统一写入，可撤销。", TextWrapping = TextWrapping.Wrap });
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
                for (var i = 0; i < source.Length; i++)
                {
                    token.ThrowIfCancellationRequested(); var cue = source[i];
                    if (!SentenceSegmentation.IsEnglish(cue.Text) || cue.Translation.Length > 0) continue;
                    SetAiFeedback($"恢复标点 · {i + 1}/{source.Length}", (i + 1d) / source.Length);
                    var answer = await model.GenerateAsync(settings.SummaryModel, "Restore English punctuation and capitalization only. Keep every word, spelling, number and word order exactly unchanged. Do not translate, explain, add, remove or substitute words. Output only the punctuated text. /no_think", cue.Text, null, token);
                    source[i] = SentenceSegmentation.RestorePunctuation(cue, AiService.StripThinking(answer));
                }
            }
            var optimized = await Task.Run(() => SentenceSegmentation.Optimize(source, pauses), token); token.ThrowIfCancellationRequested();
            // Atomic commit; cancellation/failure leaves the session untouched.
            if (current?.Id == session.Id) Snapshot();
            session.Segments = new(optimized); store.SaveSession(session);
            if (current?.Id == session.Id) { TrackSegments(); RefreshSubtitles(); Dirty(); }
            SetAiFeedback($"断句完成 · {source.Length} → {optimized.Count} 条字幕", 1);
        }
        catch (OperationCanceledException) { SetAiFeedback("已停止断句，原字幕已保留。"); }
        finally { if (gateHeld) AiService.GpuGate.Release(); aiBusy = false; aiCancellation.Dispose(); aiCancellation = null; EndAiFeedback(); UpdateEditState(); }
    });
}
