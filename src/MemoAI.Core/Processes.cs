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
    public Task RunAsync(JobRequest request, Func<JobEvent, Task> onEvent, CancellationToken ct) => RunCoreAsync(request, onEvent, ct, null);
    public Task RunLiveAsync(JobRequest request, System.Threading.Channels.ChannelReader<LiveAudioClip> clips, Func<JobEvent, Task> onEvent, CancellationToken ct) => RunCoreAsync(request, onEvent, ct,
        async (input, token) => { await foreach (var clip in clips.ReadAllAsync(token)) { await input.WriteLineAsync(Json.Write(clip)); await input.FlushAsync(token); } });
    private async Task RunCoreAsync(JobRequest request, Func<JobEvent, Task> onEvent, CancellationToken ct, Func<StreamWriter, CancellationToken, Task>? writeClips)
    {
        var asr = request.Operation.StartsWith("transcribe", StringComparison.Ordinal) && AsrModels.IsManifest(request.Settings.TranscriptionModelPath);
        var onnx = asr || request.Operation is "tts" or "diarize";
        var providers = !onnx ? new[] { "cpu" } : request.Settings.Backend switch
        {
            ComputeBackend.Cuda => ["cuda"], ComputeBackend.DirectML => ["directml"], ComputeBackend.Cpu => ["cpu"],
            ComputeBackend.Vulkan => throw new NotSupportedException("ONNX 模型请使用 CUDA 或 DirectML；Vulkan 适用于 Whisper。"),
            _ => new[] { "cuda", "directml", "cpu" }
        };
        foreach (var provider in providers)
        {
            var runtime = provider == "cpu" ? null : SherpaRuntimes.Find(paths, provider);
            if (provider != "cpu" && runtime is null)
            {
                if (request.Settings.Backend != ComputeBackend.Auto) throw new FileNotFoundException($"缺少 {provider} 运行时，请在模型商店的「语音辅助」分类安装 GPU 组件。");
                continue;
            }
            if (runtime is not null && !SherpaRuntimes.SupportsOperation(runtime, request.Operation))
            {
                const string message = "当前 ARM64 DirectML 配音未通过验证，请使用 CUDA 或 CPU。";
                if (request.Settings.Backend != ComputeBackend.Auto) throw new NotSupportedException(message);
                await onEvent(new("warning", message)); continue;
            }
            if (asr && runtime is not null && !SherpaRuntimes.Supports(runtime, AsrModels.ReadInstallation(request.Settings.TranscriptionModelPath).Id))
            {
                var modelId = AsrModels.ReadInstallation(request.Settings.TranscriptionModelPath).Id;
                var message = modelId == "funasr-nano-fp16" ? "当前 FunASR Nano FP16 未通过 GPU 验证，请选择 INT8 版本使用 DirectML，或选择 CPU。"
                    : runtime.Provider == "cuda" ? "当前 FunASR Nano INT8 CUDA 未通过内容校验，请使用 DirectML 或 CPU。" : "当前 ARM64 DirectML 运行时与 SenseVoice INT8 不兼容，请使用 SenseVoice FP32、CUDA 或 CPU。";
                if (request.Settings.Backend != ComputeBackend.Auto) throw new NotSupportedException(message);
                await onEvent(new("warning", message)); continue;
            }
            try { await RunProcessAsync(request, onEvent, ct, writeClips, runtime); return; }
            catch (Exception ex) when (ex is not OperationCanceledException && request.Settings.Backend == ComputeBackend.Auto && provider != "cpu" && writeClips is null)
            { await onEvent(new("warning", $"{provider} 未能完成，正在尝试下一后端：{ex.Message}")); }
        }
    }
    private async Task RunProcessAsync(JobRequest request, Func<JobEvent, Task> onEvent, CancellationToken ct, Func<StreamWriter, CancellationToken, Task>? writeClips, SherpaRuntime? runtime)
    {
        var finalOutput = request.Operation == "tts" ? request.OutputPath ?? Path.Combine(request.WorkDirectory, "dub.wav") : null;
        var stagedOutput = finalOutput is null ? null : finalOutput + "." + Guid.NewGuid().ToString("N") + ".unverified.wav";
        if (stagedOutput is not null) request = request with { OutputPath = stagedOutput };
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "worker", "MemoAI.Worker.exe"),
            Path.Combine(AppContext.BaseDirectory, "MemoAI.Worker.exe"),
            Path.Combine(paths.EngineRoot, "..", "artifacts", AppPaths.Rid, "worker", "MemoAI.Worker.exe")
        };
        var worker = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("未找到后台工作程序，请运行 scripts/build.ps1。");
        PeArchitecture.RequireCompatible(worker);
        if (runtime is not null) worker = await Task.Run(() => SherpaRuntimes.PrepareWorker(paths, runtime, worker), ct);
        var start = new ProcessStartInfo(worker) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.Environment["WINWHISPER_ONNX_PROVIDER"] = runtime?.Provider ?? "cpu";
        start.Environment["WINWHISPER_MEDIA_RID"] = AppPaths.Rid;
        using var process = new Process { StartInfo = start };
        process.Start();
        using var inputCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var stop = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var errorTask = process.StandardError.ReadToEndAsync();
        var inputTask = Task.Run(async () =>
        {
            try { await process.StandardInput.WriteLineAsync(Json.Write(request)); await process.StandardInput.FlushAsync(ct); if (writeClips is not null) await writeClips(process.StandardInput, inputCancellation.Token); }
            finally { process.StandardInput.Close(); }
        }, ct);
        JobEvent? completed = null;
        try
        {
        while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            JobEvent e;
            try { e = Json.Read<JobEvent>(line); } catch (System.Text.Json.JsonException) { continue; }
            if (e.Type == "error") throw new InvalidOperationException(e.Message);
            if (e.Type == "completed") completed = e;
            else await onEvent(e);
        }
        await process.WaitForExitAsync(ct);
        if (writeClips is not null && !inputTask.IsCompleted) inputCancellation.Cancel();
        try { await inputTask; } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        var error = await errorTask;
        if (process.ExitCode != 0 || completed is null || SherpaRuntimes.IsNativeFailure(error)) throw new InvalidOperationException($"后台任务未成功完成（退出码 {process.ExitCode}）。{(error.Length <= 3000 ? error : error[..1500] + "\n…\n" + error[^1500..])}");
        if (runtime is not null && request.Operation == "transcribe")
        {
            var cache = SherpaTranscription.CheckpointRoot(request, runtime.Provider); Directory.CreateDirectory(cache);
            await File.WriteAllTextAsync(Path.Combine(cache, "verified.json"), Json.Write(new { runtime.Provider, verified = true }), ct);
        }
        if (stagedOutput is not null)
        {
            if (!File.Exists(stagedOutput)) throw new InvalidDataException("配音进程未生成有效的待验证文件。");
            File.Move(stagedOutput, finalOutput!, true); completed = completed with { Path = finalOutput };
        }
        await onEvent(completed);
        }
        finally
        {
            inputCancellation.Cancel();
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            try { await inputTask; } catch (Exception) { }
            if (stagedOutput is not null)
            {
                try { if (!process.HasExited) await process.WaitForExitAsync(); if (File.Exists(stagedOutput)) File.Delete(stagedOutput); }
                catch (IOException) { }
            }
        }
    }
}
