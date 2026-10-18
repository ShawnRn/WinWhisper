using System.Diagnostics;
using System.Text;

namespace MemoAI.Core;

public sealed record ProcessResult(int ExitCode, string Output, string Error);

public static class Processes
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
        string? directory = null, Action<string>? onOutput = null, Action<string>? onError = null, CancellationToken ct = default)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory ?? Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException($"无法启动：{executable}");
        using var registration = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = new StringBuilder(); var error = new StringBuilder();
        async Task ReadAsync(StreamReader reader, StringBuilder buffer, Action<string>? callback)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (buffer.Length > 256_000) buffer.Remove(0, 128_000);
                buffer.AppendLine(line); callback?.Invoke(line);
            }
        }
        await Task.WhenAll(ReadAsync(process.StandardOutput, output, onOutput), ReadAsync(process.StandardError, error, onError), process.WaitForExitAsync());
        ct.ThrowIfCancellationRequested();
        return new(process.ExitCode, output.ToString(), error.ToString());
    }
    public static void EnsureSuccess(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException($"{operation}失败（退出码 {result.ExitCode}）。\n{result.Error[^Math.Min(3000, result.Error.Length)..]}");
    }
}

public sealed class WorkerClient(AppPaths paths)
{
    public async Task RunAsync(JobRequest request, Func<JobEvent, Task> onEvent, CancellationToken ct)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "worker", "MemoAI.Worker.exe"),
            Path.Combine(AppContext.BaseDirectory, "MemoAI.Worker.exe"),
            Path.Combine(paths.EngineRoot, "..", "artifacts", AppPaths.Rid, "worker", "MemoAI.Worker.exe")
        };
        var worker = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("未找到后台工作程序，请运行 scripts/build.ps1。");
        PeArchitecture.RequireCompatible(worker);
        var start = new ProcessStartInfo(worker) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        using var process = new Process { StartInfo = start };
        process.Start();
        using var stop = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(Json.Write(request)); await process.StandardInput.FlushAsync(); process.StandardInput.Close();
        var completed = false;
        while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            JobEvent e;
            try { e = Json.Read<JobEvent>(line); } catch (System.Text.Json.JsonException) { continue; }
            await onEvent(e);
            if (e.Type == "error") throw new InvalidOperationException(e.Message);
            if (e.Type == "completed") completed = true;
        }
        await process.WaitForExitAsync(ct);
        var error = await errorTask;
        if (process.ExitCode != 0 || !completed) throw new InvalidOperationException($"后台任务异常退出。{error[^Math.Min(2000, error.Length)..]}");
    }
}
