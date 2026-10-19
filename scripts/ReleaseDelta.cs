// Original MIT implementation of the Git binary-patch / pack-delta formats.
// Format references: https://git-scm.com/docs/gitformat-pack and Git's base85 alphabet.
// This does not include Git implementation code and is only used for release transport.
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class ReleaseBinaryDelta
{
    const int Limit = 256 * 1024 * 1024;
    const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz!#$%&()*+-;<=>?@^_`{|}~";

    static InvalidDataException Bad(string message) => new InvalidDataException(message);
    static int Byte(ReadOnlySpan<byte> data, ref int position)
    {
        if (position >= data.Length) throw Bad("Truncated delta instruction");
        return data[position++];
    }
    static long Number(ReadOnlySpan<byte> data, ref int position)
    {
        long value = 0;
        for (int shift = 0; shift <= 28; shift += 7)
        {
            int next = Byte(data, ref position);
            value |= (long)(next & 127) << shift;
            if ((next & 128) == 0)
            {
                if (value > int.MaxValue) throw Bad("Installer size exceeds the transport limit");
                return value;
            }
        }
        throw Bad("Invalid size encoding");
    }
    static void Apply(Stream source, Stream target, ReadOnlySpan<byte> delta, long expected)
    {
        int position = 0;
        if (Number(delta, ref position) != source.Length || Number(delta, ref position) != expected)
            throw Bad("Delta source or target size mismatch");
        long written = 0;
        byte[] buffer = new byte[1024 * 1024];
        while (position < delta.Length)
        {
            int instruction = Byte(delta, ref position);
            if ((instruction & 128) != 0)
            {
                long offset = 0, count = 0;
                for (int part = 0; part < 4; part++)
                    if ((instruction & (1 << part)) != 0) offset |= (long)Byte(delta, ref position) << (8 * part);
                for (int part = 0; part < 3; part++)
                    if ((instruction & (1 << (part + 4))) != 0) count |= (long)Byte(delta, ref position) << (8 * part);
                if (count == 0) count = 65536;
                if (offset > source.Length - count || count > expected - written) throw Bad("Copy exceeds source or output bounds");
                source.Position = offset;
                long remaining = count;
                while (remaining > 0)
                {
                    int block = (int)Math.Min(remaining, buffer.Length);
                    source.ReadExactly(buffer.AsSpan(0, block));
                    target.Write(buffer, 0, block);
                    remaining -= block;
                }
                written += count;
            }
            else
            {
                int count = instruction;
                if (count == 0 || count > delta.Length - position || count > expected - written) throw Bad("Invalid literal instruction");
                target.Write(delta.Slice(position, count));
                position += count; written += count;
            }
        }
        if (written != expected) throw Bad("Incomplete reconstructed installer");
    }
    public static void Restore(string baseline, string patch, string output, string targetName, long expectedBytes)
    {
        if (expectedBytes <= 0 || expectedBytes > int.MaxValue || new FileInfo(patch).Length > 512L * 1024 * 1024)
            throw Bad("Transport size limit exceeded");
        if (Path.GetFileName(targetName) != targetName ||
            string.Equals(Path.GetFullPath(baseline), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw Bad("Invalid output path");
        using var reader = new StreamReader(patch, Encoding.ASCII);
        if (reader.ReadLine() != "diff --git a/" + targetName + " b/" + targetName) throw Bad("Unexpected patch filename");
        if (!(reader.ReadLine() ?? "").StartsWith("index ") || reader.ReadLine() != "GIT binary patch") throw Bad("Invalid binary patch header");
        string header = reader.ReadLine() ?? "";
        bool isDelta = header.StartsWith("delta ");
        if (!isDelta && !header.StartsWith("literal ")) throw Bad("Unsupported patch method");
        if (!int.TryParse(header.Substring(isDelta ? 6 : 8), out int length) || length <= 0 || length > Limit)
            throw Bad("Decoded patch exceeds the transport limit");
        using var compressed = new MemoryStream();
        byte[] lineBytes = new byte[52];
        bool ended = false;
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) { ended = true; break; }
            int count = line[0] >= 'A' && line[0] <= 'Z' ? line[0] - 'A' + 1 :
                line[0] >= 'a' && line[0] <= 'z' ? line[0] - 'a' + 27 : 0;
            if (count == 0 || line.Length != 1 + ((count + 3) / 4) * 5) throw Bad("Invalid base85 line");
            int decoded = 0;
            for (int group = 1; group < line.Length; group += 5)
            {
                ulong word = 0;
                for (int digit = 0; digit < 5; digit++)
                {
                    int value = Alphabet.IndexOf(line[group + digit]);
                    if (value < 0) throw Bad("Invalid base85 alphabet");
                    word = word * 85 + (uint)value;
                }
                if (word > uint.MaxValue) throw Bad("Base85 overflow");
                for (int part = 0; part < 4 && decoded < count; part++) lineBytes[decoded++] = (byte)(word >> (24 - 8 * part));
            }
            if (compressed.Length + count > Limit + 1024L * 1024) throw Bad("Compressed patch exceeds the transport limit");
            compressed.Write(lineBytes, 0, count);
        }
        if (!ended || reader.ReadLine() != null) throw Bad("Incomplete or additional patch content");
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        byte[] data = new byte[length];
        zlib.ReadExactly(data);
        if (zlib.ReadByte() != -1) throw Bad("Unexpected decoded patch size");
        using var source = File.OpenRead(baseline);
        using var target = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
        if (isDelta) Apply(source, target, data, expectedBytes);
        else
        {
            if (data.Length != expectedBytes) throw Bad("Literal output size mismatch");
            target.Write(data);
        }
    }
    public static void SelfTest()
    {
        byte[] original = Encoding.ASCII.GetBytes("abc");
        using var source = new MemoryStream(original);
        using var output = new MemoryStream();
        Apply(source, output, new byte[] {3,5,0x90,3,2,100,101}, 5);
        if (Encoding.ASCII.GetString(output.ToArray()) != "abcde") throw Bad("Copy/literal fixture failed");
        foreach (var invalid in new byte[][] {new byte[]{3,1,0},new byte[]{3,1,0x91,5,1},new byte[]{3,2,1,100},new byte[]{4,1,1,100}})
        {
            bool rejected = false;
            try { using var target = new MemoryStream(); Apply(new MemoryStream(original), target, invalid, invalid[1]); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw Bad("Invalid delta fixture was accepted");
        }
        using var large = new MemoryStream(new byte[65536]);
        using var copied = new MemoryStream();
        Apply(large, copied, new byte[]{128,128,4,128,128,4,128}, 65536);
        if (copied.Length != 65536) throw Bad("Default copy length fixture failed");
    }
}
