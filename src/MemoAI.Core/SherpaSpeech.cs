using System.Security.Cryptography;
using System.Text;
using SherpaOnnx;

namespace MemoAI.Core;

public sealed class SherpaSpeech(AppPaths paths) : ISpeechSynthesizer
{
    public async Task SynthesizeAsync(JobRequest request, Func<JobEvent, Task> progress, CancellationToken ct)
    {
        var settings = request.Settings;
        foreach (var file in new[] { settings.TtsModelPath, settings.TtsVoicesPath, settings.TtsTokensPath })
            if (!File.Exists(file)) throw new FileNotFoundException("请先在设置中配置 Kokoro 中英模型包（model.onnx、voices.bin、tokens.txt）。", file);
        var config = new OfflineTtsConfig();
        config.Model.Kokoro.Model = settings.TtsModelPath; config.Model.Kokoro.Voices = settings.TtsVoicesPath;
        config.Model.Kokoro.Tokens = settings.TtsTokensPath; config.Model.Kokoro.DataDir = settings.TtsDataDirectory;
        config.Model.Kokoro.Lexicon = settings.TtsLexiconPath; config.Model.NumThreads = 2;
        config.Model.Provider = SherpaRuntimes.ProfilingProvider(request.WorkDirectory, out var profile);
        var backend = SherpaRuntimes.Provider == "cpu" ? "Cpu" : SherpaRuntimes.Provider == "cuda" ? "Cuda" : "DirectML";
        var device = AppPaths.Rid + " CPU";
        if (profile is not null)
        {
            await progress(new("progress", "验证配音 GPU…", Backend: "Pending"));
            using (var probe = new OfflineTts(config))
            {
                var audio = probe.GenerateWithConfig("Hello.", new OfflineTtsGenerationConfig { Sid = settings.TtsVoice, Speed = 1 }, new OfflineTtsCallbackProgressWithArg((_, _, _, _) => ct.IsCancellationRequested ? 0 : 1));
                ct.ThrowIfCancellationRequested();
            }
            device = SherpaRuntimes.CompleteProof(profile); config.Model.Provider = SherpaRuntimes.Provider;
        }
        using var tts = new OfflineTts(config);
        var segments = request.InputSegments?.OrderBy(s => s.Start).ToArray() ?? [new SubtitleSegment { Start = 0, End = double.MaxValue, Text = request.Text ?? "" }];
        if (segments.Length == 0) throw new InvalidOperationException("没有可以合成的字幕。");
        var directory = Path.Combine(request.WorkDirectory, "dub"); Directory.CreateDirectory(directory);
        var output = request.OutputPath ?? Path.Combine(request.WorkDirectory, "dub.wav");
        var pending = output + ".pending.wav";
        var clips = new List<(SubtitleSegment Segment, string Path)>();
        var media = new MediaTools(paths);
        for (var i = 0; i < segments.Length; i++)
        {
            ct.ThrowIfCancellationRequested(); var s = segments[i];
            var text = string.IsNullOrWhiteSpace(s.Translation) ? s.Text : s.Translation;
            if (string.IsNullOrWhiteSpace(text)) continue;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{text}|{settings.TtsModelPath}|{settings.TtsVoice}|{s.Start}|{s.End}|{SherpaRuntimes.Provider}")))[..24];
            var raw = Path.Combine(directory, hash + "-raw.wav"); var clip = Path.Combine(directory, hash + ".wav");
            if (!File.Exists(clip))
            {
                var generation = new OfflineTtsGenerationConfig { Sid = settings.TtsVoice, Speed = 1 };
                var callback = new OfflineTtsCallbackProgressWithArg((_, _, _, _) => ct.IsCancellationRequested ? 0 : 1);
                var audio = tts.GenerateWithConfig(text, generation, callback);
                if (!audio.SaveToWaveFile(raw)) throw new IOException("无法保存合成语音。");
                var duration = await media.ProbeAsync(raw, ct);
                var slot = s.End - s.Start;
                var filters = new List<string>();
                var ratio = slot > 0 && slot < double.MaxValue && duration > slot ? duration / slot : 1;
                while (ratio > 2) { filters.Add("atempo=2"); ratio /= 2; }
                if (ratio > 1.001) filters.Add("atempo=" + MediaTools.N(ratio));
                var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", raw };
                if (filters.Count > 0) args.AddRange(["-af", string.Join(',', filters)]);
                args.AddRange(["-ac", "1", "-ar", "24000", "-c:a", "pcm_s16le", clip]);
                Processes.EnsureSuccess(await Processes.RunAsync(media.Ffmpeg, args, ct: ct), "配音时间对齐");
            }
            clips.Add((s, clip));
            await progress(new("progress", $"配音 {i + 1}/{segments.Length}", (double)(i + 1) / segments.Length, Backend: backend, Device: device));
        }
        // Write directly to the timeline file so long media does not allocate a full-duration audio buffer.
        await using (var timeline = new FileStream(pending, FileMode.Create, FileAccess.ReadWrite))
        {
            timeline.Write(new byte[44]);
            foreach (var (segment, clip) in clips)
            {
                using var source = File.OpenRead(clip); using var reader = new BinaryReader(source);
                var (offset, length) = FindWaveData(reader);
                timeline.Position = 44 + (long)(Math.Max(0, segment.Start) * 24000) * 2;
                source.Position = offset; var remaining = length; var buffer = new byte[65536];
                while (remaining > 0) { var n = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct); if (n == 0) break; await timeline.WriteAsync(buffer.AsMemory(0, n), ct); remaining -= n; }
            }
            var fullDuration = File.Exists(request.MediaPath) ? await media.ProbeAsync(request.MediaPath, ct) : 0;
            timeline.SetLength(Math.Max(timeline.Length, 44 + (long)(fullDuration * 24000) * 2));
            timeline.Position = 0; WriteWaveHeader(timeline, checked((int)(timeline.Length - 44)), 24000);
        }
        File.Move(pending, output, true);
        await progress(new("completed", "配音已生成并对齐字幕时间", 1, Backend: backend, Device: device, Path: output));
    }
    public async Task DiarizeAsync(JobRequest request, Func<JobEvent, Task> progress, CancellationToken ct)
    {
        if (!File.Exists(request.Settings.SpeakerSegmentationModel) || !File.Exists(request.Settings.SpeakerEmbeddingModel))
            throw new FileNotFoundException("请先配置说话人分段和声纹模型。");
        var config = new OfflineSpeakerDiarizationConfig();
        config.Segmentation.Pyannote.Model = request.Settings.SpeakerSegmentationModel;
        config.Embedding.Model = request.Settings.SpeakerEmbeddingModel;
        config.Clustering.NumClusters = -1; config.Clustering.Threshold = 0.5f;
        var wav = Path.Combine(request.WorkDirectory, "diarization.wav");
        await new MediaTools(paths).ExtractAudioAsync(request.MediaPath, wav, 0, null, ct);
        float[] samples;
        using (var wave = new BinaryReader(File.OpenRead(wav)))
        {
            var (offset, length) = FindWaveData(wave);
            wave.BaseStream.Position = offset; samples = new float[length / 2];
            for (var i = 0; i < samples.Length; i++) samples[i] = wave.ReadInt16() / 32768f;
        }
        config.Segmentation.Provider = config.Embedding.Provider = SherpaRuntimes.ProfilingProvider(request.WorkDirectory, out var profile);
        var backend = SherpaRuntimes.Provider == "cpu" ? "Cpu" : SherpaRuntimes.Provider == "cuda" ? "Cuda" : "DirectML";
        var device = AppPaths.Rid + " CPU";
        if (profile is not null)
        {
            await progress(new("progress", "验证说话人模型 GPU…", Backend: "Pending"));
            using (var probe = new OfflineSpeakerDiarization(config)) _ = probe.Process(samples[..Math.Min(samples.Length, 48000)]);
            device = SherpaRuntimes.CompleteProof(profile);
            config.Segmentation.Provider = config.Embedding.Provider = SherpaRuntimes.Provider;
        }
        using var diarization = new OfflineSpeakerDiarization(config);
        var result = diarization.Process(samples);
        var segments = request.InputSegments?.Select(s => s.Copy()).ToArray() ?? [];
        foreach (var s in segments)
        {
            var best = result.OrderByDescending(r => Math.Max(0, Math.Min(r.End, s.End) - Math.Max(r.Start, s.Start))).FirstOrDefault();
            if (best is not null && Math.Min(best.End, s.End) > Math.Max(best.Start, s.Start)) s.Speaker = $"说话人 {best.Speaker + 1}";
        }
        File.Delete(wav);
        await progress(new("completed", "说话人区分完成", 1, Segments: segments, Backend: backend, Device: device));
    }
    private static (long Offset, long Length) FindWaveData(BinaryReader reader)
    {
        reader.BaseStream.Position = 12;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4)); var size = reader.ReadUInt32();
            if (id == "data") return (reader.BaseStream.Position, size);
            reader.BaseStream.Position += size + size % 2;
        }
        throw new InvalidDataException("WAV 缺少音频数据块。");
    }
    private static void WriteWaveHeader(Stream stream, int dataSize, int rate)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(dataSize + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataSize);
    }
}
