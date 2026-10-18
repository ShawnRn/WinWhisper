using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private sealed record ModelChoice(string Name, string Path);
    private sealed record LanguageChoice(string Name, string Code);
    private static readonly LanguageChoice[] languages = [new("自动检测（含中英混合）", "auto"), new("中文", "zh"), new("English", "en"), new("日本語", "ja"), new("한국어", "ko"), new("Français", "fr"), new("Deutsch", "de"), new("Español", "es"), new("Русский", "ru"), new("Português", "pt"), new("العربية", "ar")];
    private bool loadingTranscription;
    private JobRecord? workspaceJob;
    private DateTime lastProgressRefresh;
    private string? cancelRequestedJob;
    private bool transcriptionFlyoutOpen;
    private void ToggleTranscriptionSettings_Click(object sender, RoutedEventArgs e)
    {
        if (transcriptionFlyoutOpen || TranscriptionSettingsButton.IsChecked != true) TranscriptionFlyout.Hide();
        else TranscriptionFlyout.ShowAt(TranscriptionSettingsButton);
    }
    private void CloseTranscriptionSettings_Click(object sender, RoutedEventArgs e) => TranscriptionFlyout.Hide();
    private void TranscriptionFlyout_Opening(object sender, object e)
    {
        TranscriptionFlyout.AreOpenCloseAnimationsEnabled = !settings.ReduceMotion && motionSettings.AnimationsEnabled;
    }
    private void TranscriptionFlyout_Opened(object sender, object e)
    { transcriptionFlyoutOpen = true; TranscriptionSettingsButton.IsChecked = true; }
    private void TranscriptionFlyout_Closed(object sender, object e)
    {
        CloseTranscriptionDropdowns(); transcriptionFlyoutOpen = false; TranscriptionSettingsButton.IsChecked = false;
    }
    private void CloseTranscriptionDropdowns(ComboBox? except = null)
    {
        foreach (var combo in new[] { TranscriptionModel, TranscriptionLanguage, TranscriptionBackend })
            if (!ReferenceEquals(combo, except)) combo.IsDropDownOpen = false;
    }
    private void TranscriptionDropdown_Opened(object? sender, object e) => CloseTranscriptionDropdowns(sender as ComboBox);

    private void LoadTranscriptionOptions()
    {
        if (current is null) return;
        loadingTranscription = true;
        try
        {
            var options = current.Transcription ?? TranscriptionOptions.FromSettings(settings);
            var models = AsrModels.All.Where(m => m.Id != "funasr-nano-fp16" && AsrModels.IsInstalled(m, paths.ModelRoot)).Select(m => new ModelChoice(m.Name + " · ONNX", m.ManifestPath(paths.ModelRoot))).Concat(ModelCatalog.Items.Where(m => m.Id.StartsWith("whisper-") && File.Exists(Path.Combine(paths.ModelRoot, m.FileName)))
                .Select(m => new ModelChoice(m.Name.Replace(" · 推荐", ""), Path.Combine(paths.ModelRoot, m.FileName)))).ToList();
            if (options.ModelPath.Length > 0 && !models.Any(m => m.Path.Equals(options.ModelPath, StringComparison.OrdinalIgnoreCase))) models.Add(new(TranscriptionEngines.ModelName(options.ModelPath), options.ModelPath));
            TranscriptionModel.ItemsSource = models;
            TranscriptionModel.SelectedItem = models.FirstOrDefault(m => m.Path.Equals(options.ModelPath, StringComparison.OrdinalIgnoreCase));
            TranscriptionModel.PlaceholderText = "请先下载或选择模型";
            var choices = languages.ToList();
            if (!choices.Any(l => l.Code == options.Language)) choices.Add(new(options.Language, options.Language));
            TranscriptionLanguage.ItemsSource = choices; TranscriptionLanguage.SelectedItem = choices.First(l => l.Code == options.Language);
            TranscriptionBackend.SelectedIndex = (int)options.Backend;
            TranscriptionThreads.Value = options.Threads; TranscriptionBeam.Value = options.BeamSize; TranscriptionChunk.Value = options.ChunkSeconds;
            TranscriptionMaxLength.Value = options.MaxLength; TranscriptionTemperature.Value = options.Temperature;
            TranscriptionVad.IsChecked = options.EnableVad; TranscriptionPrompt.Text = options.Prompt;
            UpdateTranscriptionCapabilities();
            UpdateTranscriptionHeader();
        }
        finally { loadingTranscription = false; }
    }
    private TranscriptionOptions ReadTranscriptionOptions() => new()
    {
        ModelPath = (TranscriptionModel.SelectedItem as ModelChoice)?.Path ?? "",
        Language = (TranscriptionLanguage.SelectedItem as LanguageChoice)?.Code ?? "auto",
        Backend = (ComputeBackend)Math.Max(0, TranscriptionBackend.SelectedIndex), EnableVad = TranscriptionVad.IsChecked == true,
        Threads = (int)TranscriptionThreads.Value, BeamSize = (int)TranscriptionBeam.Value, ChunkSeconds = (int)TranscriptionChunk.Value,
        MaxLength = (int)TranscriptionMaxLength.Value, Temperature = TranscriptionTemperature.Value, Prompt = TranscriptionPrompt.Text.Trim()
    };
    private void SaveTranscriptionOptions()
    {
        if (loadingTranscription || current is null || !double.IsFinite(TranscriptionThreads.Value) || !double.IsFinite(TranscriptionBeam.Value) || !double.IsFinite(TranscriptionChunk.Value) || !double.IsFinite(TranscriptionMaxLength.Value) || !double.IsFinite(TranscriptionTemperature.Value)) return;
        current.Transcription = ReadTranscriptionOptions(); Dirty(); UpdateTranscriptionHeader();
    }
    private void UpdateTranscriptionHeader()
    {
        TranscriptionSettingsSummary.Text = $"{(TranscriptionModel.SelectedItem as ModelChoice)?.Name ?? "尚未选择模型"} · {(TranscriptionLanguage.SelectedItem as LanguageChoice)?.Name ?? "自动检测"} · {(TranscriptionBackend.SelectedItem as ComboBoxItem)?.Content}";
    }
    private void TranscriptionOption_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!loadingTranscription && ReferenceEquals(sender, TranscriptionModel) && AsrModels.IsManifest((TranscriptionModel.SelectedItem as ModelChoice)?.Path ?? ""))
        {
            loadingTranscription = true; TranscriptionBackend.SelectedIndex = 0; loadingTranscription = false;
        }
        UpdateTranscriptionCapabilities(); SaveTranscriptionOptions();
    }
    private void UpdateTranscriptionCapabilities()
    {
        if (TranscriptionModel is null || TranscriptionBeam is null) return;
        var onnx = AsrModels.IsManifest((TranscriptionModel.SelectedItem as ModelChoice)?.Path ?? "");
        string? modelId = null;
        if (onnx && TranscriptionModel.SelectedItem is ModelChoice choice) try { modelId = AsrModels.ReadInstallation(choice.Path).Id; } catch (Exception) { }
        TranscriptionBeam.IsEnabled = TranscriptionChunk.IsEnabled = TranscriptionMaxLength.IsEnabled = TranscriptionTemperature.IsEnabled = !onnx;
        TranscriptionVad.Content = onnx ? "过滤近静音（能量检测）" : "检测静音（Silero VAD）";
        ((ComboBoxItem)TranscriptionBackend.Items[2]).IsEnabled = !onnx;
        ((ComboBoxItem)TranscriptionBackend.Items[1]).IsEnabled = !onnx || SherpaRuntimes.Supports(new("cuda", "win-x64", []), modelId ?? "");
        ((ComboBoxItem)TranscriptionBackend.Items[4]).IsEnabled = onnx && SherpaRuntimes.Supports(new("directml", AppPaths.Rid, []), modelId ?? "");
        if (TranscriptionBackend.SelectedItem is ComboBoxItem { IsEnabled: false }) TranscriptionBackend.SelectedIndex = 0;
    }
    private void TranscriptionNumber_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args) => SaveTranscriptionOptions();
    private void TranscriptionCheck_Changed(object sender, RoutedEventArgs e) => SaveTranscriptionOptions();
    private void TranscriptionPrompt_Changed(object sender, TextChangedEventArgs e) => SaveTranscriptionOptions();
    private async void ChooseTranscriptionModel_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        var files = await PickFilesAsync(".bin"); if (files.Count == 0 || current is null) return;
        SaveTranscriptionOptions(); current.Transcription ??= TranscriptionOptions.FromSettings(settings); current.Transcription.ModelPath = files[0];
        Dirty(); LoadTranscriptionOptions();
    });
    private void ResetTranscriptionOptions_Click(object sender, RoutedEventArgs e)
    {
        if (current is null) return; current.Transcription = null; Dirty(); LoadTranscriptionOptions();
    }
    private async Task StartTranscriptionAsync()
    {
        RequireSession();
        if (jobs.Any(j => j.SessionId == current!.Id && j.Operation.StartsWith("transcribe", StringComparison.Ordinal) && j.State is JobState.Queued or JobState.Running)) { RefreshTranscriptionProgress(); return; }
        var options = ReadTranscriptionOptions();
        _ = options.ApplyTo(settings);
        current!.Transcription = options; Dirty();
        if (!File.Exists(options.ModelPath)) throw new FileNotFoundException("请在本页选择已下载的转写模型，或打开模型商店下载。");
        if (AsrModels.IsManifest(options.ModelPath)) _ = SherpaTranscription.Configuration(options.ModelPath, options.ApplyTo(settings));
        if (current.Segments.Count > 0 && !await ConfirmAsync("重新转写", "新转写结果会替换当前字幕。已有字幕可先导出备份。", "重新转写")) return;
        await SaveCurrentAsync();
        await scheduler.EnqueueAsync(current, "transcribe"); TranscriptionFlyout.Hide(); RefreshTranscriptionProgress();
    }
    private void RefreshTranscriptionProgress()
    {
        if (current is null) { TranscriptionProgressCard.Visibility = Visibility.Collapsed; return; }
        var matching = jobs.Where(j => j.SessionId == current.Id && j.Operation.StartsWith("transcribe", StringComparison.Ordinal)).ToArray();
        workspaceJob = matching.FirstOrDefault(j => j.State is JobState.Queued or JobState.Running) ?? matching.FirstOrDefault();
        var job = workspaceJob;
        TranscriptionProgressCard.Visibility = job is null ? Visibility.Collapsed : Visibility.Visible;
        var active = job?.State is JobState.Queued or JobState.Running;
        StartTranscribeButton.IsEnabled = !active;
        StartTranscribeButton.Content = active ? "转写中…" : current.Segments.Count > 0 ? "重新转写" : "开始转写";
        if (job is null) return;
        TranscriptionStage.Text = job.State switch { JobState.Queued => "等待转写 · 正在等待计算资源", JobState.Running => "正在转写 · " + job.Message, JobState.Completed => $"转写完成 · {current.Segments.Count} 条字幕", JobState.Cancelled => "已取消 · 已完成片段保留", JobState.Interrupted => "转写已中断 · 可从任务详情恢复", _ => "转写失败 · " + job.Message };
        TranscriptionPercent.Visibility = TranscriptionProgress.Visibility = TranscriptionRunDetails.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        TranscriptionPercent.Text = job.State == JobState.Queued ? "等待中" : $"{Math.Clamp(job.Progress, 0, 1):P0}";
        TranscriptionProgress.Value = Math.Clamp(job.Progress, 0, 1) * 100;
        TranscriptionProgress.IsIndeterminate = active && job.Progress <= 0;
        CancelTranscription.Visibility = active ? Visibility.Visible : Visibility.Collapsed; CancelTranscription.IsEnabled = active;
        if (active && cancelRequestedJob == job.Id) { CancelTranscription.IsEnabled = false; TranscriptionStage.Text = "正在取消 · 已完成片段将保留"; }
        var config = job.Request?.Settings;
        var backend = job.Backend.Length == 0 ? "等待后端初始化" : job.Device.Length == 0 && active ? job.Backend + "（初始化）" : job.Backend;
        var elapsed = active && job.StartedAt.HasValue ? (DateTimeOffset.Now - job.StartedAt.Value).TotalSeconds : job.ElapsedSeconds;
        TranscriptionRunDetails.Text = $"{Path.GetFileNameWithoutExtension(config?.TranscriptionModelPath ?? "")} · 语言 {config?.Language ?? "auto"} · {backend}" + (job.Device.Length > 0 ? " · " + job.Device.Split(',')[0] : "") + (elapsed.HasValue ? $" · 已用 {TimeSpan.FromSeconds(Math.Max(0, elapsed.Value)):hh\\:mm\\:ss}" : "") + (job.RealTimeFactor.HasValue ? $" · 实时率 {job.RealTimeFactor:0.00}" : "");
    }
    private void CancelTranscription_Click(object sender, RoutedEventArgs e)
    {
        if (workspaceJob is null) return; cancelRequestedJob = workspaceJob.Id; scheduler.Cancel(workspaceJob.Id); CancelTranscription.IsEnabled = false; TranscriptionStage.Text = "正在取消 · 已完成片段将保留";
    }
    private void TranscriptionDetails_Click(object sender, RoutedEventArgs e)
    {
        var id = workspaceJob?.Id; Queue_Click(sender, e); if (id is not null) JobList.SelectedIndex = jobs.FindIndex(j => j.Id == id);
    }
}
