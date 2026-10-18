using System.Security.Cryptography;
using System.Text.Json;

namespace MemoAI.Core;

public sealed record ModelDownload(string Id, string Name, string Repository, string FileName, string Purpose);
public sealed record ModelMetadata(long Size, string Sha256);

public static class ModelCatalog
{
    public static readonly ModelDownload[] Items =
    [
        new("whisper-turbo", "Whisper large-v3-turbo · 推荐", "ggerganov/whisper.cpp", "ggml-large-v3-turbo.bin", "多语言转写"),
        new("whisper-large", "Whisper large-v3 · 精度优先", "ggerganov/whisper.cpp", "ggml-large-v3.bin", "多语言转写"),
        new("whisper-small", "Whisper small · 轻量", "ggerganov/whisper.cpp", "ggml-small.bin", "多语言转写"),
        new("whisper-tiny", "Whisper tiny · 快速验证", "ggerganov/whisper.cpp", "ggml-tiny.bin", "多语言转写"),
        new("vad", "Silero VAD · 静音检测", "ggml-org/whisper-vad", "ggml-silero-v6.2.0.bin", "VAD"),
        new("qwen", "Qwen3 8B Q4_K_M · 独立本地 AI", "Qwen/Qwen3-8B-GGUF", "Qwen3-8B-Q4_K_M.gguf", "翻译与总结")
    ];
}

public sealed class ModelDownloader : IDisposable
{
    private readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    public async Task<ModelMetadata> MetadataAsync(ModelDownload model, CancellationToken ct)
    {
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
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://huggingface.co/{model.Repository}/resolve/main/{Uri.EscapeDataString(model.FileName)}");
            if (offset > 0) request.Headers.Range = new(offset, null);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
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
