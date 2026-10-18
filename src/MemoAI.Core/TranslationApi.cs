using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MemoAI.Core;

// This provider is created only for an explicitly confirmed translation action.
// Local chat/summarization never routes through this endpoint.
public sealed class TranslationApi : ILocalLanguageModelProvider, IDisposable
{
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly string apiKey;
    public static Uri ValidateEndpoint(string apiBase)
    {
        if (!Uri.TryCreate(apiBase.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.IsLoopback && uri.Scheme == "http"))
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("翻译 API 地址必须使用 HTTPS（本机测试可用 HTTP），且不包含密钥、查询参数或片段。请填写以 /v1 等版本路径结尾的基础地址。");
        return new Uri(uri, "chat/completions");
    }
    public TranslationApi(string apiBase, string key, HttpMessageHandler? handler = null)
    {
        endpoint = ValidateEndpoint(apiBase); apiKey = key;
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) };
    }
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => throw new NotSupportedException("请填写服务提供方的模型名称。");
    public async Task<string> GenerateAsync(string model, string system, string prompt, Func<string, Task>? onDelta, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("请先填写翻译 API 模型名称。");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (apiKey.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new { model, messages = new[] { new { role = "system", content = system + "\n" + ChineseTypography.Instructions }, new { role = "user", content = prompt } }, stream = false });
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        // Do not echo provider bodies: they may repeat credentials or private text.
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"翻译 API 请求失败（HTTP {(int)response.StatusCode}）。请检查地址、模型、额度和凭据。", null, response.StatusCode);
        await response.Content.LoadIntoBufferAsync(1024 * 1024, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var result = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException("翻译 API 返回了空内容。");
        ct.ThrowIfCancellationRequested(); if (onDelta is not null) await onDelta(result); return result;
    }
    public void Dispose() => http.Dispose();
}
