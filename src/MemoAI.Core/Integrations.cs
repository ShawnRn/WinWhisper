using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MemoAI.Core;

public static class Integrations
{
    public static async Task<string> ExportObsidianAsync(MediaSession session, string folder, CancellationToken ct)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("请先选择已有的 Obsidian 库目录。");
        var name = string.Concat(session.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (name.Length > 100) name = name[..100];
        var output = Path.Combine(folder, $"{name}-{session.Id[..8]}.md");
        await File.WriteAllTextAsync(output, Subtitles.Export(session, "md"), ct); return output;
    }
    public static async Task<string> ExportNotionAsync(MediaSession session, string parentPage, string token, CancellationToken ct)
    {
        if (!Guid.TryParse(parentPage.Replace("-", ""), out var page)) throw new ArgumentException("Notion 父页面 ID 无效。");
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("请先配置 Notion integration token。");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
        var text = Subtitles.Export(session, "md");
        var children = Enumerable.Range(0, (int)Math.Ceiling(text.Length / 1800d)).Select(i => (object)new
        {
            @object = "block", type = "paragraph", paragraph = new { rich_text = new[] { new { type = "text", text = new { content = text.Substring(i * 1800, Math.Min(1800, text.Length - i * 1800)) } } } }
        }).ToList();
        using var response = await http.PostAsJsonAsync("https://api.notion.com/v1/pages", new
        {
            parent = new { page_id = page.ToString() }, properties = new { title = new { type = "title", title = new[] { new { type = "text", text = new { content = session.Title[..Math.Min(500, session.Title.Length)] } } } } }, children = children.Take(100)
        }, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Notion 导出失败：{await response.Content.ReadAsStringAsync(ct)}");
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var id = json.RootElement.GetProperty("id").GetString();
        for (var offset = 100; offset < children.Count; offset += 100)
        {
            using var append = await http.PatchAsJsonAsync($"https://api.notion.com/v1/blocks/{id}/children", new { children = children.Skip(offset).Take(100) }, ct);
            if (!append.IsSuccessStatusCode) throw new InvalidOperationException("Notion 页面已创建，但后续内容写入失败，请检查网络后重新导出。页面 ID：" + id);
        }
        return json.RootElement.GetProperty("url").GetString() ?? "";
    }
}
