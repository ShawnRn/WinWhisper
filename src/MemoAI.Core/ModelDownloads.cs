using System.Security.Cryptography;
using System.Text.Json;

namespace MemoAI.Core;

public sealed record ModelDownload(string Id, string Name, string Repository, string FileName, string Purpose, long Size = 0, string Sha256 = "", string? Url = null);
public sealed record ModelMetadata(long Size, string Sha256);

public static class ModelCatalog
{
    public static readonly ModelDownload[] Items =
    [
        new("whisper-turbo", "Whisper large-v3-turbo · 推荐", "ggerganov/whisper.cpp", "ggml-large-v3-turbo.bin", "多语言转写", 1624555275, "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69"),
        new("whisper-large", "Whisper large-v3 · 精度优先", "ggerganov/whisper.cpp", "ggml-large-v3.bin", "多语言转写", 3095033483, "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2"),
        new("whisper-small", "Whisper small · 轻量", "ggerganov/whisper.cpp", "ggml-small.bin", "多语言转写", 487601967, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"),
        new("whisper-tiny", "Whisper tiny · 快速验证", "ggerganov/whisper.cpp", "ggml-tiny.bin", "多语言转写", 77691713, "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21"),
        new("vad", "Silero VAD · 静音检测", "ggml-org/whisper-vad", "ggml-silero-v6.2.0.bin", "VAD", 885098, "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987"),
        new("qwen", "Qwen3 8B Q4_K_M · 独立本地 AI", "Qwen/Qwen3-8B-GGUF", "Qwen3-8B-Q4_K_M.gguf", "翻译与总结", 5027783488, "d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785")
    ];
}

public sealed class ModelDownloader : IDisposable
{
    private readonly HttpClient http;
    public ModelDownloader(HttpMessageHandler? handler = null)
    {
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = Timeout.InfiniteTimeSpan;
    }
    public async Task<ModelMetadata> MetadataAsync(ModelDownload model, CancellationToken ct)
    {
        if (model.Size > 0 && model.Sha256.Length == 64) return new(model.Size, model.Sha256);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://huggingface.co/api/models/{model.Repository}/tree/main?recursive=false&expand=false");
        using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        foreach (var file in json.RootElement.EnumerateArray())
            if (file.GetProperty("path").GetString() == model.FileName)
            {
                var sha = file.TryGetProperty("lfs", out var lfs) && lfs.TryGetProperty("oid", out var oid) ? oid.GetString() ?? "" : "";
                if (sha.Length != 64) throw new InvalidDataException("模型源未提供 SHA-256，不能验证下载。");
                return new(file.GetProperty("size").GetInt64(), sha);
            }
        throw new FileNotFoundException("模型源中没有找到指定文件，请检查模型目录版本。");
    }
    public async Task<string> DownloadAsync(ModelDownload model, ModelMetadata metadata, string directory, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, model.FileName); var partial = file + ".partial";
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset > metadata.Size) { File.Delete(partial); offset = 0; }
        if (File.Exists(file) && new FileInfo(file).Length == metadata.Size)
        {
            using var existing = File.OpenRead(file);
            if (Convert.ToHexString(await SHA256.HashDataAsync(existing, ct)).Equals(metadata.Sha256, StringComparison.OrdinalIgnoreCase)) return file;
        }
        if (offset < metadata.Size)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, model.Url ?? $"https://huggingface.co/{model.Repository}/resolve/main/{Uri.EscapeDataString(model.FileName)}");
            if (offset > 0) request.Headers.Range = new(offset, null);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
            if (response.StatusCode == System.Net.HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From != offset)
                throw new InvalidDataException("下载源返回了错误的续传位置，请重试。");
            if (response.StatusCode != System.Net.HttpStatusCode.PartialContent) offset = 0;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);
            var buffer = new byte[131072]; int count; var written = offset;
            while ((count = await source.ReadAsync(buffer, ct)) > 0) { await target.WriteAsync(buffer.AsMemory(0, count), ct); written += count; progress?.Report((double)written / metadata.Size); }
        }
        using (var stream = File.OpenRead(partial))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            if (stream.Length != metadata.Size || !hash.Equals(metadata.Sha256, StringComparison.OrdinalIgnoreCase))
            { stream.Dispose(); File.Delete(partial); throw new InvalidDataException("模型大小或 SHA-256 校验失败，请重新下载。"); }
        }
        File.Move(partial, file, true); progress?.Report(1); return file;
    }
    public void Dispose() => http.Dispose();
}
