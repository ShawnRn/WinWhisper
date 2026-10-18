using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, CancellationTokenSource> modelDownloads = [];
    private readonly Dictionary<string, (double Progress, string Message)> modelProgress = [];
    private readonly Dictionary<string, (TextBlock Label, ProgressBar Bar, StackPanel Panel, Button Cancel)> modelCardProgress = [];
    private readonly Dictionary<string, (TextBlock Badge, Button Apply)> modelCards = [];
    private readonly Dictionary<string, (Button Translate, Button Summary)> ollamaButtons = [];
    private string[] ollamaModels = [];
    private async void ModelStore_Click(object sender, RoutedEventArgs e)
    {
        if (StoreCards.Children.Count == 0) BuildModelStore(); else RefreshModelStoreStates();
        ShowPage(ModelStoreView);
        if (ollamaModels.Length == 0)
        {
            try
            {
                var snapshot = Json.Read<AppSettings>(Json.Write(settings)); snapshot.ModelProvider = LanguageModelKind.Ollama;
                using var local = new LocalLanguageModelProvider(paths, snapshot); ollamaModels = (await local.ListModelsAsync(CancellationToken.None)).ToArray();
                if (ModelStoreView.Visibility == Visibility.Visible) BuildModelStore();
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or ArgumentException) { }
        }
    }
    private void StoreFilter_Changed(object sender, TextChangedEventArgs e) { if (store is not null) BuildModelStore(); }
    private void StoreCategory_Changed(object sender, SelectionChangedEventArgs e) { if (store is not null) BuildModelStore(); }
    private void BuildModelStore()
    {
        StoreCards.Children.Clear(); modelCardProgress.Clear(); modelCards.Clear(); ollamaButtons.Clear();
        var filter = StoreCategory.SelectedIndex; var query = StoreSearch.Text.Trim();
        var models = StoreModels.All.Where(m =>
            (query.Length == 0 || $"{m.Name} {m.Subtitle} {m.Description}".Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (filter == 0 || filter == 4 && StoreModels.IsInstalled(m, settings, paths.ModelRoot) || filter == 1 && m.Category == "音视频转写" || filter == 2 && m.Category == "翻译与总结" || filter == 3 && m.Category == "配音与说话人")).ToArray();
        foreach (var model in models) StoreCards.Children.Add(ModelCard(model));
        if (filter is 0 or 2 or 4)
        {
            var title = new TextBlock { Text = "已有的 Ollama 模型", Style = (Style)Application.Current.Resources["SectionTitle"], Margin = new Thickness(0, 12, 0, 0) }; StoreCards.Children.Add(title);
            if (ollamaModels.Length == 0) StoreCards.Children.Add(new TextBlock { Text = "点击「刷新 Ollama」读取本机模型，无需重复下载。", Style = (Style)Application.Current.Resources["Muted"] });
            foreach (var name in ollamaModels.Where(n => query.Length == 0 || n.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
                var translate = new Button { Content = settings.ModelProvider == LanguageModelKind.Ollama && settings.TranslationModel == name ? "✓ 翻译模型" : "用于翻译", MinWidth = 100 };
                var summary = new Button { Content = settings.ModelProvider == LanguageModelKind.Ollama && settings.SummaryModel == name ? "✓ 总结模型" : "用于总结", MinWidth = 100 };
                ollamaButtons[name] = (translate, summary);
                translate.Click += async (_, _) => await SafeAsync(() => UseOllamaModelAsync(name, true)); summary.Click += async (_, _) => await SafeAsync(() => UseOllamaModelAsync(name, false));
                Grid.SetColumn(translate, 1); Grid.SetColumn(summary, 2); row.Children.Add(translate); row.Children.Add(summary);
                StoreCards.Children.Add(new Border { Style = (Style)Application.Current.Resources["Card"], Padding = new Thickness(18), Child = row });
            }
        }
        if (models.Length == 0 && filter is 1 or 3) StoreCards.Children.Add(new TextBlock { Text = "没有匹配的模型。", Style = (Style)Application.Current.Resources["Muted"] });
        RefreshModelStoreStates();
    }
    private Border ModelCard(StoreModel model)
    {
        var installed = StoreModels.IsInstalled(model, settings, paths.ModelRoot); var applied = StoreModels.IsApplied(model, settings, paths.ModelRoot);
        var grid = new Grid { ColumnSpacing = 22, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var details = new StackPanel { Spacing = 8 };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        heading.Children.Add(new TextBlock { Text = model.Name, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var badge = new TextBlock { Text = applied ? "使用中" : installed ? "已下载" : model.Badge, FontSize = 10, Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"] };
        heading.Children.Add(new Border { Background = (Brush)Application.Current.Resources["AccentFillColorTertiaryBrush"], CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 3, 7, 3), Child = badge });
        details.Children.Add(heading);
        details.Children.Add(new TextBlock { Text = model.Subtitle, FontSize = 12, Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"] });
        details.Children.Add(new TextBlock { Text = model.Description, Style = (Style)Application.Current.Resources["Muted"], FontSize = 13 });
        details.Children.Add(new TextBlock { Text = $"{model.Size}  ·  {model.Memory}  ·  完全本地", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        grid.Children.Add(details);
        var button = new Button { Content = applied ? "已应用" : installed ? "应用模型" : "下载并应用", IsEnabled = !applied && !modelDownloads.ContainsKey(model.Id), VerticalAlignment = VerticalAlignment.Center, Style = installed ? null : (Style)Application.Current.Resources["AccentButtonStyle"] };
        button.Click += async (_, _) => await SafeAsync(async () =>
        {
            if (aiBusy) throw new InvalidOperationException("请等待当前 AI 任务完成后再切换模型。");
            if (StoreModels.IsInstalled(model, settings, paths.ModelRoot)) { StoreModels.Apply(model, settings, paths.ModelRoot); SaveAppliedModel(); return; }
            await DownloadStoreModelAsync(model);
        });
        Grid.SetColumn(button, 1); grid.Children.Add(button);
        modelCards[model.Id] = (badge, button);
        var line = new StackPanel { Spacing = 7, Visibility = Visibility.Collapsed }; var label = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap }; line.Children.Add(label);
        var bar = new ProgressBar { Height = 4 }; line.Children.Add(bar); Grid.SetRow(line, 1); grid.Children.Add(line);
        var cancel = new Button { Content = "暂停", VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        cancel.Click += (_, _) => modelDownloads.GetValueOrDefault(model.Id)?.Cancel(); Grid.SetRow(cancel, 1); Grid.SetColumn(cancel, 1); grid.Children.Add(cancel);
        modelCardProgress[model.Id] = (label, bar, line, cancel);
        return new Border { Style = (Style)Application.Current.Resources["Card"], Padding = new Thickness(22), Child = grid };
    }
    private void RefreshModelStoreStates()
    {
        foreach (var model in StoreModels.All)
        {
            if (modelCards.TryGetValue(model.Id, out var card))
            {
                var installed = StoreModels.IsInstalled(model, settings, paths.ModelRoot); var applied = StoreModels.IsApplied(model, settings, paths.ModelRoot);
                card.Badge.Text = applied ? "使用中" : installed ? "已下载" : model.Badge;
                card.Apply.Content = applied ? "已应用" : installed ? "应用模型" : "下载并应用";
                card.Apply.IsEnabled = !applied && !modelDownloads.ContainsKey(model.Id);
            }
            if (modelCardProgress.TryGetValue(model.Id, out var progress))
            {
                var hasProgress = modelProgress.TryGetValue(model.Id, out var state);
                progress.Panel.Visibility = hasProgress ? Visibility.Visible : Visibility.Collapsed;
                progress.Cancel.Visibility = modelDownloads.ContainsKey(model.Id) ? Visibility.Visible : Visibility.Collapsed;
                if (hasProgress) { progress.Label.Text = state.Message; progress.Bar.IsIndeterminate = state.Progress < 0; progress.Bar.Value = Math.Max(0, state.Progress) * 100; }
            }
        }
        foreach (var (name, buttons) in ollamaButtons)
        {
            buttons.Translate.Content = settings.ModelProvider == LanguageModelKind.Ollama && settings.TranslationModel == name ? "✓ 翻译模型" : "用于翻译";
            buttons.Summary.Content = settings.ModelProvider == LanguageModelKind.Ollama && settings.SummaryModel == name ? "✓ 总结模型" : "用于总结";
        }
    }
    private void SaveAppliedModel()
    {
        store.SaveSettings(settings);
        UpdateAiModelLabel();
        if (!aiBusy) { provider?.Dispose(); provider = null; }
        RefreshModelStoreStates(); Status("模型已应用，下一个任务会使用新模型。");
    }
    private async Task UseOllamaModelAsync(string name, bool translation)
    {
        if (aiBusy) throw new InvalidOperationException("请等待当前 AI 任务完成。");
        if (settings.ModelProvider == LanguageModelKind.Ollama && (translation ? settings.TranslationModel : settings.SummaryModel) == name) return;
        settings.ModelProvider = LanguageModelKind.Ollama;
        if (translation) settings.TranslationModel = name; else settings.SummaryModel = name;
        SaveAppliedModel(); await Task.CompletedTask;
    }
    private async void RefreshOllama_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        var snapshot = Json.Read<AppSettings>(Json.Write(settings)); snapshot.ModelProvider = LanguageModelKind.Ollama;
        using var local = new LocalLanguageModelProvider(paths, snapshot); ollamaModels = (await local.ListModelsAsync(CancellationToken.None)).ToArray();
        BuildModelStore(); Status($"发现 {ollamaModels.Length} 个本地 Ollama 模型");
    });
    private async Task DownloadStoreModelAsync(StoreModel model)
    {
        if (modelDownloads.ContainsKey(model.Id)) return;
        using var cancellation = new CancellationTokenSource(); modelDownloads[model.Id] = cancellation;
        modelProgress[model.Id] = (-1, "正在读取模型信息…"); RefreshModelStoreStates();
        try
        {
            if (model.Download is not null)
            {
                using var downloader = new ModelDownloader(); var metadata = await downloader.MetadataAsync(model.Download, cancellation.Token);
                modelProgress[model.Id] = (-1, $"正在连接下载源 · {metadata.Size / 1048576d:0} MB"); RefreshModelStoreStates();
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(paths.ModelRoot))!);
                if (drive.AvailableFreeSpace < metadata.Size + 256 * 1024 * 1024) throw new IOException("模型目录所在磁盘空间不足。");
                var lastUpdate = DateTime.UtcNow;
                var progress = new Progress<double>(p =>
                {
                    if (!modelDownloads.ContainsKey(model.Id)) return;
                    if ((DateTime.UtcNow - lastUpdate).TotalMilliseconds < 250 && p < 1) return; lastUpdate = DateTime.UtcNow;
                    modelProgress[model.Id] = (p, $"{p:P0} · {metadata.Size / 1048576d:0} MB · 完成后自动校验并应用");
                    if (modelCardProgress.TryGetValue(model.Id, out var card)) { card.Label.Text = modelProgress[model.Id].Message; card.Bar.IsIndeterminate = false; card.Bar.Value = p * 100; }
                });
                await downloader.DownloadAsync(model.Download, metadata, paths.ModelRoot, progress, cancellation.Token);
                StoreModels.Apply(model, settings, paths.ModelRoot);
            }
            else if (model.Id == "kokoro") await InstallSpeechPackAsync(false, cancellation.Token);
            else if (model.Id == "speakers") await InstallSpeakerPackAsync(false, cancellation.Token);
            SaveAppliedModel(); modelProgress[model.Id] = (1, "下载完成，模型已应用 ✓");
        }
        catch (OperationCanceledException) { modelProgress[model.Id] = (0, "下载已暂停；再次点击可继续。"); }
        catch (Exception ex) { modelProgress[model.Id] = (0, ex.Message); Status("模型下载失败：" + ex.Message); }
        finally { modelDownloads.Remove(model.Id); if (!closing) RefreshModelStoreStates(); }
    }
    private async void ImportModel_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        if (aiBusy) throw new InvalidOperationException("请先完成当前 AI 任务。");
        var files = await PickFilesAsync(".bin", ".gguf"); if (files.Count == 0) return;
        var source = files[0];
        if (Path.GetExtension(source).Equals(".gguf", StringComparison.OrdinalIgnoreCase)) { settings.GgufPath = source; settings.ModelProvider = LanguageModelKind.BuiltIn; }
        else if (Path.GetFileName(source).Contains("silero", StringComparison.OrdinalIgnoreCase)) { settings.VadModelPath = source; settings.EnableVad = true; }
        else settings.TranscriptionModelPath = source;
        SaveAppliedModel(); Status("本地模型已应用：" + Path.GetFileName(source));
    });
}
