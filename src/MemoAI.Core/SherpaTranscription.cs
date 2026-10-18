using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SherpaOnnx;

namespace MemoAI.Core;

public static class TranscriptionEngines
{
    public static ITranscriptionEngine Create(AppPaths paths, string modelPath) => AsrModels.IsManifest(modelPath) ? new SherpaTranscription(paths) : new WhisperEngine(paths);
    public static string ModelName(string path)
    {
        if (!AsrModels.IsManifest(path)) return Path.GetFileNameWithoutExtension(path);
        try { return AsrModels.ReadInstallation(path).Name; } catch (Exception) { return "未完整安装的 ONNX 模型"; }
    }
}

public sealed class SherpaTranscription(AppPaths paths) : ITranscriptionEngine
{
    public static string CheckpointRoot(JobRequest request, string provider)
    {
        var s = request.Settings; var model = AsrModels.ReadInstallation(s.TranscriptionModelPath);
        return Path.Combine(request.WorkDirectory, "transcription", MediaTools.CacheKey(request.MediaPath, s.TranscriptionModelPath,
            Json.Write(new { version = "sherpa-1.13.8-gpu-v1", provider, model.Sha256, s.AutoSegmentEnglish, s.Language, s.Glossary, s.EnableVad, request.RangeStart, request.RangeEnd })));
    }
    public static OfflineRecognizerConfig Configuration(string manifest, AppSettings settings)
    {
        var model = AsrModels.ReadInstallation(manifest);
        if (model.Id == "funasr-nano-fp16") throw new NotSupportedException("当前 FunASR Nano FP16 未通过内容校验，暂不提供推理。请在模型商店选择已验证的 INT8 版本。");
        if (settings.Backend == ComputeBackend.Vulkan)
            throw new NotSupportedException($"{model.Name} 使用 ONNX Runtime，请选择自动、CUDA、DirectML 或 CPU；Vulkan 适用于 Whisper。");
        if (settings.Language != "auto" && !model.LanguageCodes.Contains(settings.Language))
            throw new ArgumentException($"{model.Name} 不支持语言 {settings.Language}。支持：{model.Languages}。");
        var f = AsrModels.ResolveFiles(manifest);
        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Provider = SherpaRuntimes.Provider;
        config.ModelConfig.NumThreads = settings.TranscriptionThreads == 0 ? Math.Clamp(Environment.ProcessorCount / 2, 2, 8) : settings.TranscriptionThreads;
        config.ModelConfig.Tokens = f.GetValueOrDefault("tokens", "");
        switch (model.Family)
        {
            case "sensevoice":
                config.ModelConfig.SenseVoice.Model = f["model"]; config.ModelConfig.SenseVoice.Language = settings.Language;
                config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1; break;
            case "paraformer": config.ModelConfig.Paraformer.Model = f["model"]; break;
            case "transducer":
                config.ModelConfig.Transducer.Encoder = f["encoder"]; config.ModelConfig.Transducer.Decoder = f["decoder"]; config.ModelConfig.Transducer.Joiner = f["joiner"]; break;
            case "qwen3":
                config.ModelConfig.Qwen3Asr.ConvFrontend = f["frontend"]; config.ModelConfig.Qwen3Asr.Encoder = f["encoder"];
                config.ModelConfig.Qwen3Asr.Decoder = f["decoder"]; config.ModelConfig.Qwen3Asr.Tokenizer = f["tokenizer"];
                config.ModelConfig.Qwen3Asr.Hotwords = settings.Glossary; config.ModelConfig.Qwen3Asr.MaxNewTokens = 256; break;
            case "funasr":
                config.ModelConfig.FunAsrNano.EncoderAdaptor = f["encoder"]; config.ModelConfig.FunAsrNano.LLM = f["llm"];
                config.ModelConfig.FunAsrNano.Embedding = f["embedding"]; config.ModelConfig.FunAsrNano.Tokenizer = f["tokenizer"];
                config.ModelConfig.FunAsrNano.Language = settings.Language switch { "auto" => "", "zh" => "中文", "en" => "英文", "ja" => "日文", _ => settings.Language };
                config.ModelConfig.FunAsrNano.Itn = 1; config.ModelConfig.FunAsrNano.Hotwords = settings.Glossary; break;
        }
        return config;
    }

    public async Task TranscribeAsync(JobRequest request, Func<JobEvent, Task> progress, CancellationToken ct)
    {
        var settings = request.Settings; _ = TranscriptionOptions.FromSettings(settings).ApplyTo(settings);
        var model = AsrModels.ReadInstallation(settings.TranscriptionModelPath);
        var media = new MediaTools(paths); var duration = await media.ProbeAsync(request.MediaPath, ct);
        var begin = Math.Max(0, request.RangeStart ?? 0); var finish = Math.Min(duration, request.RangeEnd ?? duration);
        if (!double.IsFinite(finish) || finish <= begin) throw new InvalidDataException("媒体没有可处理的音频时长。");
        var watch = Stopwatch.StartNew();
        using var recognizer = new SherpaAsrSession(settings.TranscriptionModelPath, settings, request.WorkDirectory);
        await progress(new("progress", "加载 " + model.Name, Backend: "Pending", Device: "正在验证 " + SherpaRuntimes.Provider));
        await progress(new("warning", "ONNX 模型使用短音频分段。无词级时间戳的模型显示片段时间；Whisper 的 beam、温度、Silero GGML 参数不适用于此引擎。"));
        // Keep one model resident for the whole job. Bound audio memory even for multi-hour media.
        var cache = CheckpointRoot(request, SherpaRuntimes.Provider); Directory.CreateDirectory(cache);
        // GPU checkpoints become reusable only after the parent verifies a clean native exit.
        var reusable = SherpaRuntimes.Provider == "cpu" || File.Exists(Path.Combine(cache, "verified.json"));
        const int chunkSeconds = 20; var count = (int)Math.Ceiling((finish - begin) / chunkSeconds);
        var all = new List<SubtitleSegment>();
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var offset = begin + i * chunkSeconds; var length = Math.Min(chunkSeconds, finish - offset);
            var checkpoint = Path.Combine(cache, $"{i:00000}.segments.json");
            await progress(new("progress", $"{model.Name} · 片段 {i + 1}/{count}", (double)i / count, Backend: recognizer.Backend, Device: recognizer.Device));
            List<SubtitleSegment> segments;
            if (reusable && File.Exists(checkpoint)) segments = Json.Read<List<SubtitleSegment>>(await File.ReadAllTextAsync(checkpoint, ct));
            else
            {
                var wav = Path.Combine(cache, $"{i:00000}.wav");
                await media.ExtractAudioAsync(request.MediaPath, wav, offset, length, ct);
                var samples = ReadPcm16(wav);
                segments = [];
                foreach (var (start, end) in AudioRanges(samples, settings.EnableVad))
                {
                    ct.ThrowIfCancellationRequested();
                    var result = recognizer.Decode(samples[start..end]); ct.ThrowIfCancellationRequested();
                    var text = ChineseTypography.Apply(Regex.Replace(result.Text, @"<\|[^|]*\|>", "").Trim());
                    if (text.Length > 0) segments.Add(new() { Start = offset + start / 16000d, End = Math.Min(finish, offset + end / 16000d), Text = text,
                        Words = SentenceSegmentation.ReadOnnxWords(result.Tokens, result.Timestamps, result.Durations, offset + start / 16000d, Math.Min(finish, offset + end / 16000d)) });
                }
                if (settings.AutoSegmentEnglish && segments.Any(s => SentenceSegmentation.HasReliableWords(s) && SentenceSegmentation.IsEnglish(s.Text)))
                    segments = SentenceSegmentation.Optimize(segments, await media.DetectSilencesAsync(wav, ct, offset));
                var pending = checkpoint + ".pending"; await File.WriteAllTextAsync(pending, Json.Write(segments), ct); File.Move(pending, checkpoint, true); File.Delete(wav);
            }
            all.AddRange(segments);
            await progress(new("segments", "字幕已保存到检查点", (i + 1d) / count, Subtitles.Repair(all).ToArray(), recognizer.Backend, recognizer.Device));
        }
        await progress(new("completed", "转写完成", 1, Subtitles.Repair(all).ToArray(), recognizer.Backend == "Pending" ? "Cache" : recognizer.Backend, recognizer.Backend == "Pending" ? "使用检查点或静音，未执行模型" : recognizer.Device, watch.Elapsed.TotalSeconds, watch.Elapsed.TotalSeconds / (finish - begin)));
    }

    public static float[] ReadPcm16(string wav)
    {
        using var reader = new BinaryReader(File.OpenRead(wav));
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("录音不是 PCM WAV。");
        reader.BaseStream.Position = 12; var valid = false;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4)); var size = reader.ReadUInt32(); var position = reader.BaseStream.Position;
            if (size > reader.BaseStream.Length - position) throw new InvalidDataException("音频数据不完整。");
            if (id == "fmt ")
            {
                valid = reader.ReadInt16() == 1 && reader.ReadInt16() == 1 && reader.ReadInt32() == 16000;
                reader.BaseStream.Position = position + 14; valid &= reader.ReadInt16() == 16;
            }
            if (id == "data")
            {
                if (!valid || size > 16000 * 2 * 60) throw new InvalidDataException("需要不超过 60 秒的 16 kHz 单声道 PCM16 音频。");
                var samples = new float[size / 2]; for (var i = 0; i < samples.Length; i++) samples[i] = reader.ReadInt16() / 32768f; return samples;
            }
            reader.BaseStream.Position = position + size + size % 2;
        }
        throw new InvalidDataException("WAV 缺少音频数据。");
    }
    // Energy gating, not Silero VAD. Preserve quiet speech; only remove near-silence and split long pauses.
    public static IReadOnlyList<(int Start, int End)> AudioRanges(float[] samples, bool trimSilence)
    {
        if (!trimSilence) return samples.Length == 0 ? [] : [(0, samples.Length)];
        var ranges = new List<(int, int)>(); var start = -1; var lastVoice = 0;
        for (var i = 0; i < samples.Length; i += 320)
        {
            var end = Math.Min(i + 320, samples.Length); double energy = 0; for (var j = i; j < end; j++) energy += samples[j] * samples[j];
            if (energy / (end - i) > 0.000004) { if (start < 0) start = Math.Max(0, i - 3200); lastVoice = end; }
            else if (start >= 0 && i - lastVoice > 9600) { ranges.Add((start, Math.Min(samples.Length, lastVoice + 3200))); start = -1; }
        }
        if (start >= 0) ranges.Add((start, Math.Min(samples.Length, lastVoice + 3200)));
        return ranges;
    }
}
