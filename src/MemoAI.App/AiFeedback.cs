using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private readonly Stopwatch aiWatch = new();
    private Button? activeAiButton;
    private object? activeAiButtonContent;
    private string aiModelLabel = "";
    private static string AiOperationName(string operation) => operation switch { "segment" => "优化断句", "translate" => "翻译字幕", "correct" => "校对字幕", "chapters" => "生成章节", "mindmap" => "思维导图", "ask" => "内容问答", _ => "内容总结" };
    private void BeginAiFeedback(Button? button, string operation, string model)
    {
        activeAiButton = button; activeAiButtonContent = button?.Content;
        if (button is not null) button.Content = "处理中…";
        aiModelLabel = model; aiWatch.Restart();
        AiActivityRing.Visibility = AiActivityProgress.Visibility = Visibility.Visible; AiActivityRing.IsActive = true;
        SetAiFeedback(AiOperationName(operation) + " · 等待本地计算资源…");
        UpdateAiAvailability();
    }
    private void SetAiFeedback(string message, double? progress = null)
    {
        AiActivityText.Text = message;
        AiActivityProgress.IsIndeterminate = !progress.HasValue;
        if (progress.HasValue) AiActivityProgress.Value = Math.Clamp(progress.Value, 0, 1) * 100;
        RefreshAiElapsed();
    }
    private void RefreshAiElapsed()
    {
        if (aiModelLabel.Length > 0) AiActivityDetails.Text = $"{aiModelLabel} · 已用 {aiWatch.Elapsed:hh\\:mm\\:ss}";
    }
    private void EndAiFeedback()
    {
        aiWatch.Stop(); RefreshAiElapsed();
        AiActivityRing.IsActive = false; AiActivityRing.Visibility = AiActivityProgress.Visibility = Visibility.Collapsed;
        if (activeAiButton is not null) activeAiButton.Content = activeAiButtonContent;
        activeAiButton = null; UpdateAiAvailability();
    }
    private void UpdateAiAvailability()
    {
        if (AiActions is null || AskAiButton is null) return;
        var transcribing = current is not null && jobs.Any(j => j.SessionId == current.Id && j.Operation.StartsWith("transcribe", StringComparison.Ordinal) && j.State is MemoAI.Core.JobState.Queued or MemoAI.Core.JobState.Running);
        var available = !aiBusy && !transcribing && current?.Segments.Count > 0;
        foreach (var button in AiActions.Children.OfType<Button>().Where(b => b.Tag is string))
        { button.IsEnabled = available; ToolTipService.SetToolTip(button, aiBusy ? "当前 AI 任务运行中，可点击「停止 AI」取消。" : transcribing ? "请等待转写完成，避免仅处理不完整内容。" : available ? "结果显示在下方；翻译前可选择范围、语言和已配置的提供方。" : "请先转写或导入字幕。"); }
        AskAiButton.IsEnabled = available && !string.IsNullOrWhiteSpace(QuestionBox.Text);
        StopAiButton.IsEnabled = aiBusy; StopAiButton.Visibility = aiBusy ? Visibility.Visible : Visibility.Collapsed; QuestionBox.IsEnabled = !aiBusy;
        MoreAiTools.IsEnabled = available;
    }
    private void Question_Changed(object sender, TextChangedEventArgs e) => UpdateAiAvailability();
}
