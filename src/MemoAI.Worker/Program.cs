using System.Text;
using MemoAI.Core;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(false);
try
{
    if (args is ["--setup-probe", var setupEngines, var setupModels, var setupData])
    {
        var setupPaths = new AppPaths(setupData, setupEngines) { ModelRoot = setupModels };
        Console.WriteLine(Json.Write(await FirstRunSetup.InspectAsync(setupPaths, CancellationToken.None)));
        return;
    }
    var line = await Console.In.ReadLineAsync() ?? throw new InvalidDataException("缺少任务请求。");
    var request = Json.Read<JobRequest>(line);
    var paths = new AppPaths(engineRoot: string.IsNullOrWhiteSpace(request.Settings.EngineRoot) ? null : request.Settings.EngineRoot);
    Task Emit(JobEvent e) { Console.WriteLine(Json.Write(e)); return Task.CompletedTask; }
    switch (request.Operation)
    {
        case "transcribe": await TranscriptionEngines.Create(paths, request.Settings.TranscriptionModelPath).TranscribeAsync(request, Emit, CancellationToken.None); break;
        case "transcribe-live": await new LiveTranscription(paths).RunAsync(request, Console.In, Emit, CancellationToken.None); break;
        case "tts": await new SherpaSpeech(paths).SynthesizeAsync(request, Emit, CancellationToken.None); break;
        case "diarize": await new SherpaSpeech(paths).DiarizeAsync(request, Emit, CancellationToken.None); break;
        default: throw new NotSupportedException($"未知任务：{request.Operation}");
    }
}
catch (Exception ex)
{
    Console.WriteLine(Json.Write(new JobEvent("error", ex.Message)));
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
