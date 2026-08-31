using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace OBS.RecordingsTransfer.Services;

public enum MoovProbeResult
{
    /// <summary>moov atom found.</summary>
    Found,
    /// <summary>File was readable and scanned; no moov atom present.</summary>
    Missing,
    /// <summary>Could not reliably scan (locked, partial write, IO error).</summary>
    Unavailable
}

/// <summary>
/// Lightweight MP4 box walk to detect a moov atom (typical OBS remux finalizes with moov at the end).
/// </summary>
public static class Mp4MoovProbe
{
    private static readonly byte[] MoovType = Encoding.ASCII.GetBytes("moov");

    public static bool HasMoovAtom(string filePath) =>
        Probe(filePath) == MoovProbeResult.Found;

    public static MoovProbeResult Probe(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.SequentialScan);

            if (stream.Length < 16)
                return MoovProbeResult.Unavailable;

            // Fast path: check the last 2 MB for a moov fourcc (OBS remux writes moov at the end).
            if (ScanTailForMoov(stream))
                return MoovProbeResult.Found;

            // Also walk boxes from the start (covers faststart / moov-first layouts).
            stream.Position = 0;
            return WalkBoxesForMoov(stream);
        }
        catch
        {
            // In-progress copies/cuts often lock or truncate mid-read — keep waiting.
            return MoovProbeResult.Unavailable;
        }
    }

    private static bool ScanTailForMoov(FileStream stream)
    {
        const int TailBytes = 2 * 1024 * 1024;
        var length = stream.Length;
        var readLength = (int)Math.Min(TailBytes, length);
        if (readLength < 8)
            return false;

        var buffer = new byte[readLength];
        stream.Position = length - readLength;
        var read = stream.Read(buffer, 0, buffer.Length);
        return IndexOfFourCc(buffer.AsSpan(0, read), MoovType) >= 0;
    }

    private static MoovProbeResult WalkBoxesForMoov(FileStream stream)
    {
        var length = stream.Length;
        var header = new byte[16];

        while (stream.Position + 8 <= length)
        {
            var start = stream.Position;
            if (stream.Read(header, 0, 8) != 8)
                return MoovProbeResult.Unavailable;

            var size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            var type = header.AsSpan(4, 4);

            ulong boxSize = size;
            var headerSize = 8;
            if (size == 1)
            {
                if (stream.Read(header, 8, 8) != 8)
                    return MoovProbeResult.Unavailable;
                boxSize = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                boxSize = (ulong)(length - start);
            }

            if (boxSize < (ulong)headerSize)
                return MoovProbeResult.Unavailable;

            if (type.SequenceEqual(MoovType))
                return MoovProbeResult.Found;

            var next = start + (long)boxSize;
            if (next < start)
                return MoovProbeResult.Unavailable;

            // Declared box extends past EOF — typical of a file still being written.
            if (next > length)
                return MoovProbeResult.Unavailable;

            stream.Position = next;
        }

        // Consumed the whole file without seeing moov.
        return stream.Position >= length ? MoovProbeResult.Missing : MoovProbeResult.Unavailable;
    }

    private static int IndexOfFourCc(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
                return i;
        }

        return -1;
    }
}
