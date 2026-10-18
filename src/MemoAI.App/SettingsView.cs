using System.Diagnostics;
using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Security.Credentials;
using Windows.Storage.Pickers;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private readonly List<Action> settingWriters = [];
    private void BuildSettings()
    {
        settingWriters.Clear(); SettingsContent.Children.Clear();
        SettingsContent.Children.Add(new TextBlock { Text = "设置", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        SettingsContent.Children.Add(new TextBlock { Text = $"本地推理 · {AppPaths.Rid} · 模型按需下载，媒体不会自动上传。", Style = (Style)Application.Current.Resources["Muted"] });
        var save = new Button { Content = "保存设置", Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += async (_, _) => await SafeAsync(async () => { foreach (var write in settingWriters) write(); store.SaveSettings(settings); ApplySettings(); if (!aiBusy) { provider?.Dispose(); provider = null; } Status("设置已保存"); await Task.CompletedTask; });
        SettingsContent.Children.Add(save);
        var openStore = new Button { Content = "打开模型商店 · 一键下载与应用", HorizontalAlignment = HorizontalAlignment.Left };
        openStore.Click += ModelStore_Click; SettingsContent.Children.Add(openStore);
        var transcription = SettingsSection("转写与硬件加速", "自动模式优先 CUDA，失败后尝试 Vulkan 与 CPU。任务历史会记录实际后端。");
        Choice(transcription, "计算后端", new[] { "自动（CUDA → Vulkan → CPU）", "CUDA", "Vulkan", "CPU" }, (int)settings.Backend, i => settings.Backend = (ComputeBackend)i);
        TextSetting(transcription, "默认转写模型（在模型商店切换）", TranscriptionEngines.ModelName(settings.TranscriptionModelPath), _ => { }).IsReadOnly = true;
        TextSetting(transcription, "音频语言（auto / zh / en / ja 等）", settings.Language, v => settings.Language = v);
        var vad = new CheckBox { Content = "启用 VAD（需要先下载或选择 Silero 模型）", IsChecked = settings.EnableVad };
        transcription.Children.Add(vad); settingWriters.Add(() => settings.EnableVad = vad.IsChecked == true);
        FileSetting(transcription, "Silero VAD 模型", settings.VadModelPath, p => settings.VadModelPath = p, ".bin");
        var downloadSection = SettingsSection("模型下载", "先读取模型大小与 SHA-256，再由你确认下载。支持断点续传，完成后校验文件。");
        var catalog = new ComboBox { ItemsSource = ModelCatalog.Items.Select(m => m.Name).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        downloadSection.Children.Add(catalog);
        var download = new Button { Content = "查看大小并下载", HorizontalAlignment = HorizontalAlignment.Left };
        download.Click += async (_, _) => await SafeAsync(async () =>
        {
            var model = ModelCatalog.Items[catalog.SelectedIndex]; using var downloader = new ModelDownloader();
            Status("正在读取模型信息…"); var metadata = await downloader.MetadataAsync(model, CancellationToken.None);
            if (!await ConfirmAsync("下载 " + model.Name, $"用途：{model.Purpose}\n大小：{metadata.Size / 1073741824d:0.00} GB\n来源：huggingface.co/{model.Repository}\n保存：{paths.ModelRoot}\n\n下载后会验证 SHA-256。", "下载")) return;
            download.IsEnabled = false; var cancellation = new CancellationTokenSource();
            var cancel = new Button { Content = "取消下载（保留进度）" }; cancel.Click += (_, _) => cancellation.Cancel(); downloadSection.Children.Add(cancel);
            try
            {
                var output = await downloader.DownloadAsync(model, metadata, paths.ModelRoot, new Progress<double>(p => Status($"下载 {model.Name} · {p:P0}", p)), cancellation.Token);
                if (model.Id == "qwen") settings.GgufPath = output;
                else if (model.Id == "vad") settings.VadModelPath = output;
                else settings.TranscriptionModelPath = output;
                store.SaveSettings(settings); Status("模型已下载并校验：" + output); BuildSettings();
            }
            finally { cancellation.Dispose(); download.IsEnabled = true; downloadSection.Children.Remove(cancel); }
        });
        downloadSection.Children.Add(download);
        var llm = SettingsSection("本地翻译与总结", "支持已经运行的 Ollama，以及应用自己管理的 llama.cpp。服务地址限制为回环地址。");
        Choice(llm, "本地 AI 提供方", new[] { "Ollama", "内置 llama.cpp（GGUF）" }, (int)settings.ModelProvider, i => settings.ModelProvider = (LanguageModelKind)i);
        TextSetting(llm, "Ollama 地址", settings.OllamaEndpoint, v => settings.OllamaEndpoint = v.Trim());
        TextSetting(llm, "翻译模型（Ollama 模型名）", settings.TranslationModel, v => settings.TranslationModel = v.Trim());
        TextSetting(llm, "总结 / 校对 / 问答模型", settings.SummaryModel, v => settings.SummaryModel = v.Trim());
        var listModels = new Button { Content = "检测 Ollama 模型", HorizontalAlignment = HorizontalAlignment.Left };
        var modelList = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 12 };
        listModels.Click += async (_, _) => await SafeAsync(async () =>
        {
            foreach (var write in settingWriters) write();
            var snapshot = Json.Read<AppSettings>(Json.Write(settings)); snapshot.ModelProvider = LanguageModelKind.Ollama;
            using var models = new LocalLanguageModelProvider(paths, snapshot); var names = await models.ListModelsAsync(CancellationToken.None);
            modelList.Text = names.Count > 0 ? string.Join("\n", names) : "Ollama 里还没有模型。";
        });
        llm.Children.Add(listModels); llm.Children.Add(modelList);
        FileSetting(llm, "内置 GGUF 文件", settings.GgufPath, p => settings.GgufPath = p, ".gguf");
        TextSetting(llm, "翻译目标语言", settings.TargetLanguage, v => settings.TargetLanguage = v);
        TextSetting(llm, "术语表（每行：原词 = 译词）", settings.Glossary, v => settings.Glossary = v, true);
        TextSetting(llm, "总结提示词模板", settings.SummaryPrompt, v => settings.SummaryPrompt = v, true);
        var speech = SettingsSection("本地配音与说话人", "Kokoro 中英模型用于配音；Pyannote 分段模型与声纹模型用于说话人区分。配音默认优先读取译文。");
        var speechPack = new Button { Content = "下载并配置中英配音模型包…", HorizontalAlignment = HorizontalAlignment.Left };
        speechPack.Click += async (_, _) => await SafeAsync(() => InstallSpeechPackAsync()); speech.Children.Add(speechPack);
        var speakerPack = new Button { Content = "下载并配置说话人模型…", HorizontalAlignment = HorizontalAlignment.Left };
        speakerPack.Click += async (_, _) => await SafeAsync(() => InstallSpeakerPackAsync()); speech.Children.Add(speakerPack);
        FileSetting(speech, "Kokoro model.onnx", settings.TtsModelPath, p => settings.TtsModelPath = p, ".onnx");
        FileSetting(speech, "Kokoro voices.bin", settings.TtsVoicesPath, p => settings.TtsVoicesPath = p, ".bin");
        FileSetting(speech, "Kokoro tokens.txt", settings.TtsTokensPath, p => settings.TtsTokensPath = p, ".txt");
        TextSetting(speech, "espeak-ng-data 目录", settings.TtsDataDirectory, v => settings.TtsDataDirectory = v);
        TextSetting(speech, "中英词典路径（逗号分隔）", settings.TtsLexiconPath, v => settings.TtsLexiconPath = v);
        var voice = new NumberBox { Header = "音色 ID（中英模型中文音色从 46 起，默认 50）", Value = settings.TtsVoice, Minimum = 0, Maximum = 102, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        speech.Children.Add(voice); settingWriters.Add(() => settings.TtsVoice = (int)(double.IsFinite(voice.Value) ? voice.Value : 50));
        FileSetting(speech, "说话人分段模型", settings.SpeakerSegmentationModel, p => settings.SpeakerSegmentationModel = p, ".onnx");
        FileSetting(speech, "说话人声纹模型", settings.SpeakerEmbeddingModel, p => settings.SpeakerEmbeddingModel = p, ".onnx");
        var appearance = SettingsSection("界面与存储", "生成文件与缓存保存在应用数据目录；原始媒体采用路径引用。");
        Choice(appearance, "外观", new[] { "跟随系统", "浅色", "深色" }, settings.Theme == "Dark" ? 2 : settings.Theme == "Light" ? 1 : 0, i => settings.Theme = i switch { 2 => "Dark", 1 => "Light", _ => "Default" });
        var motion = new CheckBox { Content = "减少视觉效果（关闭 Mica）", IsChecked = settings.ReduceMotion }; appearance.Children.Add(motion); settingWriters.Add(() => settings.ReduceMotion = motion.IsChecked == true);
        TextSetting(appearance, "引擎根目录（包含 win-arm64 / win-x64 子目录）", paths.EngineRoot, v => settings.EngineRoot = Path.GetFullPath(v));
        TextSetting(appearance, "模型保存目录", paths.ModelRoot, v => settings.ModelRoot = Path.GetFullPath(v));
        var openData = new Button { Content = "打开应用数据目录", HorizontalAlignment = HorizontalAlignment.Left };
        openData.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", paths.DataRoot) { UseShellExecute = true }); appearance.Children.Add(openData);
        var integration = SettingsSection("导出集成", "Obsidian 写入本地文件夹；Notion 仅在你主动点击导出后发送内容。Token 保存到 Windows 凭据管理器。");
        FolderSetting(integration, "Obsidian 库目录", settings.ObsidianFolder, v => settings.ObsidianFolder = v);
        TextSetting(integration, "Notion 父页面 ID", settings.NotionParentPage, v => settings.NotionParentPage = v);
        var secret = new PasswordBox { Header = "Notion integration token（留空保留已有凭据）" }; integration.Children.Add(secret);
        settingWriters.Add(() => { if (secret.Password.Length > 0) SecretVault.Set("notion", secret.Password); });
        var footer = new TextBlock { Text = "快捷键：Ctrl+O 导入 · Ctrl+F 搜索 · Ctrl+Z 撤销 · 字幕列表空格播放/暂停 · F2 重转写片段 · Delete 删除字幕", Style = (Style)Application.Current.Resources["Muted"], FontSize = 12 };
        SettingsContent.Children.Add(footer);
        var about = SettingsSection("关于 WinWhisper", "独立开发的 Windows 本地转写与 AI 工作台。");
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        about.Children.Add(new TextBlock { Text = $"版本 {version}  ·  {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\n开发者：Shawn Rain\n开源许可：MIT（第三方组件与模型遵循各自许可）", TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        var github = new HyperlinkButton { Content = "GitHub · ShawnRn/WinWhisper", NavigateUri = new Uri("https://github.com/ShawnRn/WinWhisper"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0) };
        about.Children.Add(github);
        var notices = new Button { Content = "查看开源许可与 Credits", HorizontalAlignment = HorizontalAlignment.Left };
        notices.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", Path.Combine(AppContext.BaseDirectory, "CREDITS.md")) { UseShellExecute = true });
        about.Children.Add(notices);
    }
    private StackPanel SettingsSection(string title, string description)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SectionTitle"] });
        panel.Children.Add(new TextBlock { Text = description, Style = (Style)Application.Current.Resources["Muted"], FontSize = 12 });
        SettingsContent.Children.Add(new ContinuousSurface { Style = (Style)Application.Current.Resources["Card"], Padding = new Thickness(22), Child = panel }); return panel;
    }
    private TextBox TextSetting(StackPanel parent, string label, string value, Action<string> setter, bool multiline = false)
    {
        var text = new TextBox { Header = label, Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 80 : 0, MaxHeight = multiline ? 160 : 100, HorizontalAlignment = HorizontalAlignment.Stretch };
        parent.Children.Add(text); settingWriters.Add(() => setter(text.Text)); return text;
    }
    private void Choice(StackPanel parent, string label, string[] items, int selectedIndex, Action<int> setter)
    {
        var combo = new ComboBox { Header = label, ItemsSource = items, SelectedIndex = selectedIndex, HorizontalAlignment = HorizontalAlignment.Stretch };
        parent.Children.Add(combo); settingWriters.Add(() => setter(Math.Max(0, combo.SelectedIndex)));
    }
    private void FileSetting(StackPanel parent, string label, string value, Action<string> setter, string extension)
    {
        var row = new Grid { ColumnSpacing = 8 }; row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new TextBox { Header = label, Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        var browse = new Button { Content = "浏览", VerticalAlignment = VerticalAlignment.Bottom };
        browse.Click += async (_, _) => await SafeAsync(async () => { var files = await PickFilesAsync(extension); if (files.Count > 0) text.Text = files[0]; });
        row.Children.Add(text); Grid.SetColumn(browse, 1); row.Children.Add(browse); parent.Children.Add(row); settingWriters.Add(() => setter(text.Text.Trim()));
    }
    private void FolderSetting(StackPanel parent, string label, string value, Action<string> setter)
    {
        var text = TextSetting(parent, label, value, setter); var browse = new Button { Content = "选择文件夹" };
        browse.Click += async (_, _) => await SafeAsync(async () =>
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync(); if (folder is not null) text.Text = folder.Path;
        }); parent.Children.Add(browse);
    }
    private async Task InstallSpeechPackAsync(bool confirm = true, CancellationToken ct = default)
    {
        var directory = await DownloadReleaseAssetAsync("k2-fsa/sherpa-onnx", "tts-models", "kokoro-multi-lang-v1_1.tar.bz2", "Kokoro 中英配音模型", confirm, ct);
        if (directory is null) return;
        string Find(string name) => Directory.EnumerateFiles(directory, name, SearchOption.AllDirectories).First();
        settings.TtsModelPath = Find("model.onnx"); settings.TtsVoicesPath = Find("voices.bin"); settings.TtsTokensPath = Find("tokens.txt");
        settings.TtsDataDirectory = Directory.EnumerateDirectories(directory, "espeak-ng-data", SearchOption.AllDirectories).First();
        settings.TtsLexiconPath = Find("lexicon-us-en.txt") + "," + Find("lexicon-zh.txt"); settings.TtsVoice = 50;
        store.SaveSettings(settings); BuildSettings(); Status("中英配音模型配置完成");
    }
    private async Task InstallSpeakerPackAsync(bool confirm = true, CancellationToken ct = default)
    {
        var segmentation = await DownloadReleaseAssetAsync("k2-fsa/sherpa-onnx", "speaker-segmentation-models", "sherpa-onnx-pyannote-segmentation-3-0.tar.bz2", "说话人分段模型", confirm, ct);
        if (segmentation is null) return;
        settings.SpeakerSegmentationModel = Directory.EnumerateFiles(segmentation, "model.onnx", SearchOption.AllDirectories).First(); store.SaveSettings(settings);
        var embedding = await DownloadReleaseAssetAsync("k2-fsa/sherpa-onnx", "speaker-recongition-models", "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx", "说话人声纹模型", confirm, ct);
        if (embedding is not null) settings.SpeakerEmbeddingModel = Directory.EnumerateFiles(embedding, "*.onnx", SearchOption.AllDirectories).First();
        store.SaveSettings(settings); BuildSettings();
    }
    private async Task<string?> DownloadReleaseAssetAsync(string repository, string tag, string assetName, string name, bool confirm = true, CancellationToken ct = default)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = Timeout.InfiniteTimeSpan }; http.DefaultRequestHeaders.UserAgent.ParseAdd("WinWhisper/0.1");
        var source = $"https://github.com/{repository}/releases/download/{tag}/{assetName}";
        using var metadata = await http.SendAsync(new(System.Net.Http.HttpMethod.Head, source), ct); metadata.EnsureSuccessStatusCode();
        var size = metadata.Content.Headers.ContentLength;
        if (confirm && !await ConfirmAsync("下载 " + name, $"大小：{(size.HasValue ? (size.Value / 1048576d).ToString("0.0") + " MB" : "以实际下载为准")}\n来源：github.com/{repository}\n保存到模型目录。", "下载")) return null;
        var directory = Path.Combine(paths.ModelRoot, Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(assetName))); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, assetName); Status("正在下载 " + name + "…", 0);
        await using (var target = File.Create(file)) await (await http.GetStreamAsync(source, ct)).CopyToAsync(target, ct);
        if (size.HasValue && new FileInfo(file).Length != size.Value) throw new InvalidDataException("模型包大小不匹配。");
        if (assetName.EndsWith(".tar.bz2")) Processes.EnsureSuccess(await Processes.RunAsync("tar.exe", ["-xf", file, "-C", directory], Environment.SystemDirectory), "解压模型包");
        Status(name + "已下载"); return directory;
    }
}

internal static class SecretVault
{
    private const string Resource = "MemoAI.Native";
    public static string Get(string key) { try { var credential = new PasswordVault().Retrieve(Resource, key); credential.RetrievePassword(); return credential.Password; } catch (System.Runtime.InteropServices.COMException) { return ""; } }
    public static void Set(string key, string value) { var vault = new PasswordVault(); try { vault.Remove(vault.Retrieve(Resource, key)); } catch (System.Runtime.InteropServices.COMException) { } vault.Add(new(Resource, key, value)); }
}
