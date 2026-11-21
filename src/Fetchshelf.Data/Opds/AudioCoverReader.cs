using System.Buffers.Binary;
using System.Text;

namespace Fetchshelf.Data.Opds;

/// <summary>
/// Reads the cover embedded in an audio file: the MP4 <c>covr</c> atom of an m4b or m4a, or the ID3v2 picture of an mp3.
/// Only the parts holding the cover are read, never the audio, and nothing above <see cref="BookCoverExtractor.MaxCoverBytes"/>.
/// </summary>
public static class AudioCoverReader
{
    /// <summary>An ID3v2 tag larger than this is skipped rather than read: the cover is capped, the rest is text.</summary>
    private const int MaxId3TagBytes = BookCoverExtractor.MaxCoverBytes + (1024 * 1024);

    /// <summary>The image bytes of an m4b or m4a's cover, or null when it has none or isn't a readable MP4 file.</summary>
    public static byte[]? ReadMp4(Stream stream)
    {
        // The cover lives in moov/udta/meta/ilst/covr/data; some files have meta directly under moov.
        if (FindBox(stream, 0, stream.Length, "moov") is not { } moov)
        {
            return null;
        }

        var meta = FindBox(stream, moov.Start, moov.End, "udta") is { } udta ? FindBox(stream, udta.Start, udta.End, "meta") : null;
        meta ??= FindBox(stream, moov.Start, moov.End, "meta");
        // meta is a full box: its version and flags come before its children.
        if (meta is not { } found
            || FindBox(stream, found.Start + 4, found.End, "ilst") is not { } ilst
            || FindBox(stream, ilst.Start, ilst.End, "covr") is not { } covr
            || FindBox(stream, covr.Start, covr.End, "data") is not { } data)
        {
            return null;
        }

        // A data box starts with a type indicator and a locale, four bytes each; the image follows.
        var length = data.End - data.Start - 8;
        if (length <= 0 || length > BookCoverExtractor.MaxCoverBytes)
        {
            return null;
        }

        stream.Position = data.Start + 8;
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>The image bytes of an mp3's ID3v2 picture, the front cover when there are several; null when it has none.</summary>
    public static byte[]? ReadId3(Stream stream)
    {
        Span<byte> header = stackalloc byte[10];
        stream.Position = 0;
        if (stream.ReadAtLeast(header, 10, throwOnEndOfStream: false) < 10 || !header[..3].SequenceEqual("ID3"u8))
        {
            return null;
        }

        var version = header[3];
        var flags = header[5];
        var size = SyncSafe(header[6..10]);
        if (version is < 2 or > 4 || size <= 0 || size > MaxId3TagBytes || size > stream.Length - 10)
        {
            return null;
        }

        var tag = new byte[size];
        stream.ReadExactly(tag);
        if ((flags & 0x80) != 0 && version < 4)
        {
            tag = RemoveUnsynchronisation(tag);
        }

        var position = 0;
        if ((flags & 0x40) != 0 && version >= 3)
        {
            // An extended header: its size counts itself in 2.4, not in 2.3.
            var extended = version == 4 ? SyncSafe(tag.AsSpan(0, 4)) : (int)BinaryPrimitives.ReadUInt32BigEndian(tag) + 4;
            position = extended;
        }

        byte[]? first = null;
        var idLength = version == 2 ? 3 : 4;
        var headerLength = version == 2 ? 6 : 10;
        while (position + headerLength <= tag.Length && tag[position] != 0)
        {
            var id = Encoding.ASCII.GetString(tag, position, idLength);
            var frameSize = version switch
            {
                2 => (tag[position + 3] << 16) | (tag[position + 4] << 8) | tag[position + 5],
                3 => (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(tag.AsSpan(position + 4)), int.MaxValue),
                _ => SyncSafe(tag.AsSpan(position + 4, 4)),
            };
            var start = position + headerLength;
            if (frameSize <= 0 || frameSize > tag.Length - start)
            {
                break;
            }

            if (id is "APIC" or "PIC" && Picture(tag.AsSpan(start, frameSize), version, version == 4 ? tag[position + 9] : (byte)0) is var (type, image))
            {
                if (type == 3)
                {
                    return image;
                }

                first ??= image;
            }

            position = start + frameSize;
        }

        return first;
    }

    /// <summary>A picture frame's type and image; null for a compressed or encrypted frame or one too large.</summary>
    private static (byte Type, byte[] Image)? Picture(ReadOnlySpan<byte> frame, byte version, byte formatFlags)
    {
        if ((formatFlags & 0x0C) != 0)
        {
            return null;
        }

        byte[] data = frame.ToArray();
        if ((formatFlags & 0x02) != 0)
        {
            data = RemoveUnsynchronisation(data);
        }

        var i = (formatFlags & 0x01) != 0 ? 4 : 0;
        if (i >= data.Length)
        {
            return null;
        }

        var encoding = data[i++];
        if (version == 2)
        {
            // The image format, three letters.
            i += 3;
        }
        else
        {
            // The MIME type, always Latin-1 and ended by a zero.
            while (i < data.Length && data[i] != 0)
            {
                i++;
            }

            i++;
        }

        if (i >= data.Length)
        {
            return null;
        }

        var type = data[i++];
        // The description, ended by one zero in Latin-1 and UTF-8, by two in UTF-16.
        if (encoding is 1 or 2)
        {
            while (i + 1 < data.Length && (data[i] != 0 || data[i + 1] != 0))
            {
                i += 2;
            }

            i += 2;
        }
        else
        {
            while (i < data.Length && data[i] != 0)
            {
                i++;
            }

            i++;
        }

        var length = data.Length - i;
        return length <= 0 || length > BookCoverExtractor.MaxCoverBytes ? null : (type, data[i..]);
    }

    /// <summary>The first box of <paramref name="type"/> between <paramref name="start"/> and <paramref name="end"/>, as the range of its contents.</summary>
    private static (long Start, long End)? FindBox(Stream stream, long start, long end, string type)
    {
        Span<byte> header = stackalloc byte[16];
        var position = start;
        while (position + 8 <= end)
        {
            stream.Position = position;
            if (stream.ReadAtLeast(header[..8], 8, throwOnEndOfStream: false) < 8)
            {
                return null;
            }

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var headerLength = 8;
            if (size == 1)
            {
                if (stream.ReadAtLeast(header[8..16], 8, throwOnEndOfStream: false) < 8)
                {
                    return null;
                }

                size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(header[8..16]), long.MaxValue);
                headerLength = 16;
            }
            else if (size == 0)
            {
                size = end - position;
            }

            if (size < headerLength || position + size > end)
            {
                return null;
            }

            if (header[4..8].SequenceEqual(Encoding.ASCII.GetBytes(type)))
            {
                return (position + headerLength, position + size);
            }

            position += size;
        }

        return null;
    }

    private static int SyncSafe(ReadOnlySpan<byte> bytes) =>
        ((bytes[0] & 0x7F) << 21) | ((bytes[1] & 0x7F) << 14) | ((bytes[2] & 0x7F) << 7) | (bytes[3] & 0x7F);

    /// <summary>Undoes ID3 unsynchronisation: every 0xFF 0x00 becomes 0xFF.</summary>
    private static byte[] RemoveUnsynchronisation(byte[] bytes)
    {
        var result = new List<byte>(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            result.Add(bytes[i]);
            if (bytes[i] == 0xFF && i + 1 < bytes.Length && bytes[i + 1] == 0x00)
            {
                i++;
            }
        }

        return [.. result];
    }
}
