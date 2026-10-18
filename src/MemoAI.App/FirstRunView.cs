using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private bool setupOpen, setupBusy, setupAdvanced;
    private Expander? setupAdvancedOptions;
    private int setupStep;
    private SetupReport? setupReport;
    private string setupModelId = "whisper-small", setupLanguage = "auto";
    private bool setupCuda;
    private CancellationTokenSource? setupCancellation;
    private TextBlock? setupProgressText;
    private ProgressBar? setupProgressBar;
    private string setupError = "";

    private void OpenFirstRun()
    {
        if (setupOpen || closing) return;
        if (aiBusy || recorder is not null || jobs.Any(j => j.State is JobState.Running or JobState.Queued))
        { Status("请等待当前录音或任务结束后再运行设备设置。"); return; }
        setupOpen = true; setupStep = 0; setupError = ""; setupReport = null; setupAdvanced = false;
        FirstRunHost.Visibility = Visibility.Visible; Sidebar.IsEnabled = false; Sidebar.Visibility = Visibility.Collapsed; NavigationBack.IsEnabled = false;
        RenderFirstRun();
    }
    private TextBlock SetupText(string text, double size = 14) => new()
    { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontFamily = SettingsFont };
    private void RenderFirstRun()
    {
        FirstRunHost.Children.Clear(); setupProgressBar = null; setupProgressText = null;
        var layout = new Grid { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(28, 32, 28, 24), RowSpacing = 24 };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(SetupText($"开始使用 WinWhisper · {Math.Min(setupStep + 1, 3)} / 3", 13));
        header.Children.Add(new TextBlock { Text = setupStep switch { 0 => "欢迎使用 WinWhisper", 1 => "已经为你选好了", 2 => "准备你的转写工具", _ => "一切就绪" }, FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        layout.Children.Add(header);
        var body = new StackPanel { Spacing = 16 };
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        if (setupStep == 0)
        {
            body.Children.Add(SetupText("把音视频变成文字，留住值得留下的内容。", 18));
            body.Children.Add(SetupCard("只需准备一次", "我们会检查这台电脑，选好合适的转写工具。你只需确认下载，就可以开始。"));
            body.Children.Add(SetupText("音视频与文字保存在本机。检测不会上传电脑信息，现有内容也会保留。", 13));
        }
        else if (setupStep == 1 && setupReport is { } report)
        {
            var gpu = report.Gpus.Length == 0 ? "未检测到硬件显卡" : string.Join("\n", report.Gpus.Select(g => $"{g.Name} · 独立显存 {g.DedicatedBytes / 1073741824d:0.0} GB · 共享上限 {g.SharedBytes / 1073741824d:0.0} GB"));
            body.Children.Add(SetupText("根据这台电脑的检测结果，推荐以下方案。以后随时可以更换。"));
            var languages = new ComboBox { Header = "你通常转写什么语言？", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "自动识别（含中文、多语言）", "英语", "中文" }, SelectedIndex = setupLanguage == "en" ? 1 : setupLanguage == "zh" ? 2 : 0 };
            languages.SelectionChanged += (_, _) => { setupLanguage = languages.SelectedIndex == 1 ? "en" : languages.SelectedIndex == 2 ? "zh" : "auto"; setupModelId = FirstRunSetup.Recommend(report, setupLanguage).ModelId; setupCuda = false; RenderFirstRun(); };
            body.Children.Add(languages);
            var recommendation = FirstRunSetup.Recommend(report, setupLanguage);
            var advanced = new StackPanel { Spacing = 16 };
            var choices = StoreModels.All.Where(m => m.Category == "音视频转写" && (m.Asr is null || m.Id == "parakeet-v3" || m.Id == "sensevoice-fp32" || m.Id == "paraformer-int8") && (m.Asr is null || setupLanguage != "auto" && m.Asr.LanguageCodes.Contains(setupLanguage))).ToArray();
            if (!choices.Any(m => m.Id == setupModelId)) setupModelId = recommendation.ModelId;
            var model = new ComboBox { Header = "转写模型", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = choices.Select(m => $"{m.Name} · {m.Size}").ToArray(), SelectedIndex = Array.FindIndex(choices, m => m.Id == setupModelId) };
            model.SelectionChanged += (_, _) => { if (model.SelectedIndex < 0) return; setupModelId = choices[model.SelectedIndex].Id; setupCuda = false; RenderFirstRun(); }; advanced.Children.Add(model);
            var selectedModel = StoreModels.All.Single(m => m.Id == setupModelId);
            if (selectedModel.Asr is not null && report.Gpus.Any(g => g.Vendor == 0x10de) && SherpaRuntimes.Find(paths, "cuda") is null && SherpaRuntimes.Supports(new("cuda", "win-x64", []), setupModelId))
            {
                var cuda = new CheckBox { Content = "同时安装 ONNX CUDA 运行时（约 1.21 GB，需额外 6 GB 空间）", IsChecked = setupCuda };
                cuda.Click += (_, _) => { setupCuda = cuda.IsChecked == true; RenderFirstRun(); }; advanced.Children.Add(cuda);
            }
            var plan = FirstRunSetup.Plan(setupModelId, setupLanguage, setupCuda);
            var friendly = setupModelId switch { "whisper-tiny" => "轻量转写", "whisper-small" => "日常转写", "whisper-large" => "精细转写", "parakeet-v3" => "英语转写", _ => "高质量转写" };
            var benefit = setupModelId switch { "whisper-tiny" => "下载小、占用少，适合先试用。", "whisper-small" => "兼顾速度与效果，适合日常录音。", "parakeet-v3" => "适合英语访谈、视频和播客。", _ => "适合视频、访谈和中英混合内容。" };
            body.Children.Add(SetupCard(friendly, benefit + "\n" + selectedModel.Name));
            body.Children.Add(SetupText($"需要下载约 {(plan.DownloadBytes >= 1_000_000_000 ? $"{plan.DownloadBytes / 1_000_000_000d:0.0} GB" : $"{plan.DownloadBytes / 1_000_000d:0} MB")}，已有文件会直接校验复用。", 13));
            advanced.Children.Add(SetupText("推荐依据：" + recommendation.Reason, 13));
            advanced.Children.Add(SetupText($"{report.Cpu} · {report.LogicalCores} 逻辑核心 · {report.Architecture}\n内存 {report.MemoryBytes / 1073741824d:0.0} GB（可用 {report.AvailableMemoryBytes / 1073741824d:0.0} GB）\n{gpu}\n模型磁盘可用 {report.FreeDiskBytes / 1073741824d:0.0} GB\nCPU 运算测试：{report.DspMillionSamplesPerSecond:0} M 样本 / 秒（非转写速度 / GPU 实测）", 12));
            advanced.Children.Add(SetupText("GPU 容量仅用于推荐，实际加速由转写任务验证；共享内存上限不等于可用显存。", 12));
            advanced.Children.Add(new HyperlinkButton { Content = "模型来源与许可", NavigateUri = new Uri(selectedModel.Asr?.Source ?? $"https://huggingface.co/{selectedModel.Download!.Repository}"), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left });
            advanced.Children.Add(SetupText(string.Join("\n", plan.Files.Select(f => $"{f.Name} · {f.Size / 1048576d:0.0} MB\n来源：{new Uri(f.Url ?? $"https://huggingface.co/{f.Repository}").Host} / {f.Repository}")) + (plan.Cuda ? "\nCUDA：sherpa-onnx / llama.cpp / NVIDIA cuDNN" : ""), 12));
            var dependencyText = string.Join("\n", report.Dependencies.Select(d => $"{(d.Ready ? "✓" : "!")} {d.Name} · {d.Detail}"));
            advanced.Children.Add(SetupText("依赖检查\n" + dependencyText, 12));
            setupAdvancedOptions = new Expander { Header = "高级选项", IsExpanded = setupAdvanced, Content = advanced };
            setupAdvancedOptions.Expanding += (_, _) => setupAdvanced = true; setupAdvancedOptions.Collapsed += (_, _) => setupAdvanced = false;
            body.Children.Add(setupAdvancedOptions);
            if (report.Warnings.Length > 0) body.Children.Add(SetupText(string.Join("\n", report.Warnings), 12));
        }
        else if (setupStep == 2)
        {
            body.Children.Add(SetupText("下载完成后会自动准备好。你可以随时暂停，下次继续。", 16));
            setupProgressText = SetupText(setupBusy ? "正在准备…" : "已暂停；已下载的内容保留，点击继续。"); body.Children.Add(setupProgressText);
            setupProgressBar = new ProgressBar { IsIndeterminate = setupBusy }; body.Children.Add(setupProgressBar);
            if (!setupBusy)
            {
                var change = new HyperlinkButton { Content = "返回调整模型", Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
                change.Click += (_, _) => { setupStep = 1; setupError = ""; RenderFirstRun(); }; body.Children.Add(change);
            }
        }
        else if (setupStep == 3)
        {
            body.Children.Add(SetupCard("可以开始了", "转写工具已准备好。导入第一段音视频，或试试录音吧。"));
            body.Children.Add(SetupText("导入音视频或开始录音。其他转写、翻译与总结模型可以在模型商店按需添加。"));
        }
        if (setupError.Length > 0) body.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Message = setupError });
        var footer = new Grid { ColumnSpacing = 12 }; footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var later = new Button { Content = setupBusy ? "暂停" : setupStep == 3 ? "" : "稍后设置", Visibility = setupStep == 3 ? Visibility.Collapsed : Visibility.Visible };
        later.Click += (_, _) => { if (setupBusy) setupCancellation?.Cancel(); else FinishFirstRun("skipped"); }; footer.Children.Add(later);
        var next = new Button { Content = setupBusy ? "正在准备…" : setupStep switch { 0 => "开始设置", 1 => "一键准备", 2 => "继续", _ => "开始使用" }, IsEnabled = !setupBusy, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        next.Click += async (_, _) => await AdvanceFirstRunAsync(); Grid.SetColumn(next, 2); footer.Children.Add(next);
        Grid.SetRow(footer, 2); layout.Children.Add(footer); FirstRunHost.Children.Add(layout);
    }
    private ContinuousSurface SetupCard(string title, string detail)
    {
        var panel = new StackPanel { Spacing = 9 }; var heading = SetupText(title, 17); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        panel.Children.Add(heading); panel.Children.Add(SetupText(detail));
        return new ContinuousSurface { Style = (Style)Application.Current.Resources["Card"], Padding = new Thickness(20), Child = panel };
    }
    private async Task AdvanceFirstRunAsync()
    {
        if (setupBusy || closing) return;
        if (setupStep == 3) { FinishFirstRun("ready"); return; }
        using var cancellation = new CancellationTokenSource(); setupCancellation = cancellation; setupError = ""; setupBusy = true;
        try
        {
            if (setupStep == 0)
            {
                RenderFirstRun();
                setupReport = await FirstRunSetup.InspectInWorkerAsync(paths, cancellation.Token);
                setupModelId = FirstRunSetup.Recommend(setupReport, setupLanguage).ModelId; setupStep = 1;
            }
            else
            {
                if (setupReport?.Dependencies.Any(d => !d.Ready && d.Name is "音视频解码" or "Whisper 兼容引擎") == true)
                    throw new InvalidOperationException("随包基础依赖缺失或无法启动，请使用完整 Windows 安装程序修复安装；无需另行安装 Python、CUDA Toolkit 或系统级 FFmpeg。");
                var plan = FirstRunSetup.Plan(setupModelId, setupLanguage, setupCuda); setupStep = 2; RenderFirstRun();
                var progress = new Progress<(double Fraction, string Message)>(p =>
                { if (closing || !setupBusy) return; if (setupProgressBar is not null) { setupProgressBar.IsIndeterminate = false; setupProgressBar.Value = p.Fraction * 100; } if (setupProgressText is not null) setupProgressText.Text = p.Message; });
                var updated = await Task.Run(() => FirstRunSetup.InstallAsync(paths, settings, plan, progress, cancellation.Token), cancellation.Token);
                store.SaveSettings(updated); settings = updated; ApplySettings(); BuildSettings(); setupStep = 3;
            }
        }
        catch (OperationCanceledException) { setupError = "操作已暂停，原设置保持不变。可以继续或稍后设置。"; }
        catch (Exception ex) { setupError = ex.Message; }
        finally { setupCancellation = null; setupBusy = false; if (!closing) RenderFirstRun(); }
    }
    private void FinishFirstRun(string outcome)
    {
        if (setupBusy) return;
        try { FirstRunSetup.Complete(paths.DataRoot, outcome, setupReport); }
        catch (Exception ex) { setupError = "未能保存首次设置：" + ex.Message; RenderFirstRun(); return; }
        setupOpen = false; FirstRunHost.Visibility = Visibility.Collapsed; FirstRunHost.Children.Clear(); Sidebar.IsEnabled = true; Sidebar.Visibility = Visibility.Visible;
        UpdateHistoryButtons();
    }
}
