using System.IO.Compression;
using System.Text.Json;

namespace RedMeridian.Domain;

static class SaveCodec
{
    public const int MaxUploadBytes = 64 * 1024 * 1024;
    const int MaxJsonBytes = 256 * 1024 * 1024;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Write(Stream output, SaveData save)
    {
        using var compressed = new GZipStream(output,CompressionLevel.Optimal,leaveOpen: true);
        JsonSerializer.Serialize(compressed,save,Json);
    }

    // Historical slot paths may contain either plain JSON or gzip-compressed JSON.
    public static SaveData? Read(Stream input, bool limitExpandedSize = false)
    {
        long start = input.Position;
        bool gzip = input.ReadByte() == 0x1f && input.ReadByte() == 0x8b;
        input.Position = start;
        if (!gzip && (input.ReadByte() != 0xef || input.ReadByte() != 0xbb || input.ReadByte() != 0xbf))
            input.Position = start;
        using var compressed = gzip ? new GZipStream(input,CompressionMode.Decompress,leaveOpen: true) : null;
        var jsonInput = compressed ?? input;
        using var bounded = limitExpandedSize ? new LimitedReader(jsonInput) : null;
        return JsonSerializer.Deserialize<SaveData>(bounded ?? jsonInput,Json);
    }

    sealed class LimitedReader(Stream input) : Stream
    {
        int remaining = MaxJsonBytes;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer,int offset,int count) => Read(buffer.AsSpan(offset,count));

        public override int Read(Span<byte> buffer)
        {
            int count = input.Read(buffer[..Math.Min(buffer.Length,remaining + 1)]);
            remaining -= count;
            if (remaining < 0) throw new InvalidDataException("The expanded save exceeds 256 MiB.");
            return count;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count) => throw new NotSupportedException();
    }
}
