using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MemoAI.Core;

public sealed class LocalLanguageModelProvider : ILocalLanguageModelProvider, IDisposable
{
    private readonly AppSettings settings;
    private readonly AppPaths paths;
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private Process? server;
    private string? serverUrl;
    public LocalLanguageModelProvider(AppPaths paths, AppSettings settings) { this.paths = paths; this.settings = settings; }
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        if (settings.ModelProvider == LanguageModelKind.BuiltIn)
            return File.Exists(settings.GgufPath) ? [Path.GetFileName(settings.GgufPath)] : [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await http.GetAsync(LocalEndpoint(settings.OllamaEndpoint) + "/api/tags", timeout.Token);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
        return json.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString()!).ToArray();
    }
    private static string LocalEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("本地模型地址必须是 localhost 或回环 IP，避免将内容意外发送到外部服务。");
        return value.TrimEnd('/');
    }
    public async Task<string> GenerateAsync(string model, string system, string prompt, Func<string, Task>? onDelta, CancellationToken ct)
    {
        if (!system.Contains(ChineseTypography.Instructions, StringComparison.Ordinal)) system += "\n" + ChineseTypography.Instructions;
        var builtIn = settings.ModelProvider == LanguageModelKind.BuiltIn;
        var endpoint = builtIn ? await EnsureServerAsync(ct) : LocalEndpoint(settings.OllamaEndpoint);
        object body = builtIn
            ? new { model = "local", messages = new[] { new { role = "system", content = system }, new { role = "user", content = prompt } }, stream = true, temperature = 0.2, max_tokens = 4096 }
            : new { model, messages = new[] { new { role = "system", content = system }, new { role = "user", content = prompt } }, stream = true, think = false, keep_alive = "5m", options = new { temperature = 0.2, num_ctx = 8192, num_predict = 4096 } };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint + (builtIn ? "/v1/chat/completions" : "/api/chat")) { Content = JsonContent.Create(body, options: Json.Options) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"本地模型请求失败（{(int)response.StatusCode}）：{await response.Content.ReadAsStringAsync(ct)}");
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct), Encoding.UTF8);
        var result = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (builtIn)
            {
                if (!line.StartsWith("data: ")) continue;
                line = line[6..]; if (line == "[DONE]") break;
            }
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var json = JsonDocument.Parse(line);
            if (json.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.ToString());
            string? delta = null;
            if (builtIn)
            {
                var choices = json.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() > 0 && choices[0].GetProperty("delta").TryGetProperty("content", out var content)) delta = content.GetString();
            }
            else if (json.RootElement.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)) delta = content.GetString();
            if (!string.IsNullOrEmpty(delta)) { result.Append(delta); if (onDelta is not null) await onDelta(delta); }
        }
        if (result.Length == 0) throw new InvalidDataException("本地模型返回了空内容。");
        return result.ToString();
    }
    private async Task<string> EnsureServerAsync(CancellationToken ct)
    {
        if (server is { HasExited: false } && serverUrl is not null) return serverUrl;
        if (!File.Exists(settings.GgufPath)) throw new FileNotFoundException("请先选择或下载 GGUF 语言模型。");
        Exception? last = null;
        var backends = settings.Backend == ComputeBackend.Auto ? new[] { ComputeBackend.Cuda, ComputeBackend.Vulkan, ComputeBackend.Cpu } : [settings.Backend];
        foreach (var backend in backends)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var id = backend switch { ComputeBackend.Cuda => "llama-cuda", ComputeBackend.Vulkan => "llama-vulkan", _ => "llama-cpu" };
                var cli = paths.FindEngine(id, "llama-server.exe");
                if (backend == ComputeBackend.Cuda)
                {
                    var runtime = Path.Combine(paths.EngineRoot, AppPaths.Rid, "llama-cuda-runtime");
                    if (Directory.Exists(runtime)) foreach (var dll in Directory.EnumerateFiles(runtime, "*.dll", SearchOption.AllDirectories))
                        File.Copy(dll, Path.Combine(Path.GetDirectoryName(cli)!, Path.GetFileName(dll)), true);
                }
                using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                serverUrl = $"http://127.0.0.1:{port}";
                var start = new ProcessStartInfo(cli) { WorkingDirectory = Path.GetDirectoryName(cli)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in new[] { "-m", settings.GgufPath, "--host", "127.0.0.1", "--port", port.ToString(), "-c", "8192", "-np", "1", "-ngl", backend == ComputeBackend.Cpu ? "0" : "99", "--no-webui" }) start.ArgumentList.Add(a);
                server = Process.Start(start) ?? throw new IOException("无法启动 llama.cpp。");
                server.OutputDataReceived += (_, _) => { }; server.ErrorDataReceived += (_, _) => { }; server.BeginOutputReadLine(); server.BeginErrorReadLine();
                for (var i = 0; i < 180; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (server.HasExited) throw new IOException($"llama.cpp {backend} 启动失败。请检查模型和原生依赖。");
                    try { using var response = await http.GetAsync(serverUrl + "/health", ct); if (response.IsSuccessStatusCode) return serverUrl; }
                    catch (HttpRequestException) { }
                    await Task.Delay(500, ct);
                }
                throw new TimeoutException("本地模型加载超时。");
            }
            catch (OperationCanceledException) { StopServer(); throw; }
            catch (Exception ex) { last = ex; StopServer(); }
        }
        throw new InvalidOperationException("无法启动内置语言模型。", last);
    }
    private void StopServer() { try { if (server is { HasExited: false }) server.Kill(true); } catch (InvalidOperationException) { } server?.Dispose(); server = null; serverUrl = null; }
    public void Dispose() { StopServer(); http.Dispose(); }
}

public sealed class AiService(ILocalLanguageModelProvider provider, AppSettings settings)
{
    public static readonly SemaphoreSlim GpuGate = new(1, 1);
    public async Task TranslateAsync(IReadOnlyList<SubtitleSegment> segments, Func<int, Task> checkpoint, CancellationToken ct)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var segment = segments[i];
            var system = $"你是专业字幕译者。将用户提供的字幕翻译成{settings.TargetLanguage}。只输出该句译文，不要解释、添加引号或重复原文。保留人名、产品名、数字及语气。术语表：\n{settings.Glossary}";
            var text = await provider.GenerateAsync(settings.TranslationModel, system, segment.Text, null, ct);
            ct.ThrowIfCancellationRequested();
            segment.Translation = StripThinking(text).Trim();
            await checkpoint(i + 1);
        }
    }
    public async Task<string> SummarizeAsync(MediaSession session, string operation, Func<string, Task>? delta, CancellationToken ct)
    {
        var chunks = ChunkTranscript(session.Segments, 14000).ToArray();
        if (chunks.Length == 0) throw new InvalidOperationException("请先转写或导入字幕。");
        var system = operation switch
        {
            "chapters" => "根据带时间戳的转写，生成中文章节目录。每章包含原始时间戳、标题和一句概述。时间戳必须来自原文，不要编造。",
            "mindmap" => "根据内容生成中文思维导图。只输出 JSON 对象，格式为 {\"title\":\"主题\",\"children\":[{\"title\":\"要点\",\"children\":[]}]}。最多四层，不要 Markdown 代码块。",
            _ => settings.SummaryPrompt
        };
        if (chunks.Length == 1) return StripThinking(await provider.GenerateAsync(settings.SummaryModel, system, chunks[0], delta, ct));
        var summaries = new List<string>();
        for (var i = 0; i < chunks.Length; i++)
        {
            if (delta is not null) await delta($"\n正在处理长视频 {i + 1}/{chunks.Length}…\n");
            summaries.Add(await provider.GenerateAsync(settings.SummaryModel, "用中文提炼这段转写中的事实、观点与对应原始时间戳。不要编造。", chunks[i], null, ct));
        }
        // Recursively reduce to keep the final request within an 8K-token context.
        while (summaries.Sum(s => s.Length) > 14000 && summaries.Count > 1)
        {
            var reduced = new List<string>();
            for (var i = 0; i < summaries.Count; i += 2)
                reduced.Add(await provider.GenerateAsync(settings.SummaryModel, "合并并压缩以下摘要至 1000 字以内，保留关键事实、章节与时间戳。", string.Join("\n\n", summaries.Skip(i).Take(2)), null, ct));
            summaries = reduced;
        }
        return StripThinking(await provider.GenerateAsync(settings.SummaryModel, system, string.Join("\n\n", summaries), delta, ct));
    }
    public async Task CorrectAsync(IReadOnlyList<SubtitleSegment> segments, Func<int, Task> checkpoint, CancellationToken ct)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            segments[i].Text = StripThinking(await provider.GenerateAsync(settings.SummaryModel,
                "校对以下字幕，只修复明显错别字、标点及术语。不改变原意，不添加事实。只输出修正后原文。术语表：\n" + settings.Glossary, segments[i].Text, null, ct));
            await checkpoint(i + 1);
        }
    }
    public async Task<string> AskAsync(MediaSession session, string question, Func<string, Task>? delta, CancellationToken ct, IReadOnlyList<ChatTurn>? history = null, Func<int, int, Task>? progress = null)
    {
        var chunks = ChunkTranscript(session.Segments, 6000).ToArray();
        if (chunks.Length == 0) throw new InvalidOperationException("请先转写或导入字幕。");
        var conversation = string.Join("\n\n", (history ?? []).Where(t => t.Content.Length > 0).TakeLast(8).Select(t => $"{(t.Role == "user" ? "用户" : "助手")}：{t.Content[..Math.Min(t.Content.Length, 400)]}"));
        if (conversation.Length > 2000) conversation = conversation[^2000..];
        var context = $"历史对话（仅作追问上下文，可能包含错误，以转写证据为准）：\n{conversation}\n\n当前问题：{question}";
        var system = "根据原始转写回答问题，引用原文时间戳。理解追问中的指代。历史回答可能有误，以转写为准。不可把一个片段中未提及，推断为整个视频未提及。信息不足时说明不确定，不要虚构。" + ChineseTypography.Instructions;
        if (chunks.Length == 1) return StripThinking(await provider.GenerateAsync(settings.SummaryModel, system, $"已提供全部转写：\n{chunks[0]}\n\n{context}", delta, ct));
        var evidence = new List<string>();
        for (var i = 0; i < chunks.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (progress is not null) await progress(i + 1, chunks.Length);
            var extract = await provider.GenerateAsync(settings.SummaryModel,
                "核对当前转写片段，提取与当前问题及追问有关的事实、原文短句及时间戳，包括反证。最多 600 字。不需要给最终回答；没有证据就输出「本片段未找到相关证据」。不要根据其他片段或历史回答编造事实。" + ChineseTypography.Instructions,
                $"片段 {i + 1}/{chunks.Length}：\n{chunks[i]}\n\n{context}", null, ct);
            evidence.Add($"片段 {i + 1}/{chunks.Length} 核对结果：\n{StripThinking(extract)}");
        }
        // Reduce every batch, never discard a suffix of the transcript/evidence.
        while (evidence.Sum(s => s.Length) > 6000 && evidence.Count > 1)
        {
            var reduced = new List<string>();
            foreach (var batch in evidence.Chunk(2))
                reduced.Add(StripThinking(await provider.GenerateAsync(settings.SummaryModel,
                    "压缩以下证据至 500 字以内，保留与问题相关的事实、矛盾和原始时间戳，不添加信息。" + ChineseTypography.Instructions,
                    string.Join("\n\n", batch) + "\n\n" + context, null, ct)));
            evidence = reduced;
        }
        return StripThinking(await provider.GenerateAsync(settings.SummaryModel, system,
            $"已逐段核对全部 {session.Segments.Count} 条转写（{chunks.Length} 个片段）。以下是每段提取的证据；各段的「未找到」不代表全文没有：\n{string.Join("\n\n", evidence)}\n\n{context}", delta, ct));
    }
    public static IEnumerable<string> ChunkTranscript(IEnumerable<SubtitleSegment> segments, int maxLength)
    {
        var b = new StringBuilder();
        foreach (var s in segments)
        {
            var line = $"[{Subtitles.FormatTime(s.Start, true)}] {s.Text}\n";
            if (b.Length + line.Length > maxLength && b.Length > 0) { yield return b.ToString(); b.Clear(); }
            // Pathological single cues are split, never silently discarded.
            for (var offset = 0; offset < line.Length; offset += maxLength)
            {
                var part = line.Substring(offset, Math.Min(maxLength, line.Length - offset));
                if (b.Length + part.Length > maxLength) { yield return b.ToString(); b.Clear(); }
                b.Append(part);
            }
        }
        if (b.Length > 0) yield return b.ToString();
    }
    public static string StripThinking(string text)
    {
        var end = text.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        return ChineseTypography.Apply((end >= 0 ? text[(end + 8)..] : text).Trim());
    }
    public static string ExtractJson(string text)
    {
        text = StripThinking(text); var start = text.IndexOf('{'); var end = text.LastIndexOf('}');
        if (start < 0 || end < start) throw new InvalidDataException("模型没有返回有效的思维导图 JSON，请重试或更换总结模型。");
        var result = text[start..(end + 1)];
        using var parsed = JsonDocument.Parse(result);
        _ = Subtitles.MindMapSvg(result, "验证");
        return result;
    }
}
