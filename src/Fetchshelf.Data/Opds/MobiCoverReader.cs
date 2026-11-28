using System.Buffers.Binary;

namespace Fetchshelf.Data.Opds;

/// <summary>
/// Reads the cover out of a MOBI, AZW or AZW3 (KF8) book. All three are Palm database files: a header, then a list of
/// records. The first record holds the MOBI header, which says which record is the book's first image, and its EXTH
/// section, whose entry 201 is the cover's place among the images. The cover is that record, a plain JPEG, GIF or PNG,
/// as the images aren't encrypted even in a book with DRM.
/// </summary>
public static class MobiCoverReader
{
    private const int PdbHeaderLength = 78;
    private const int RecordCountOffset = 76;
    private const int PalmDocHeaderLength = 16;
    private const int MobiHeaderLengthOffset = 4;
    private const int FirstImageIndexOffset = 92;
    private const int ExthFlagsOffset = 112;
    private const uint ExthPresent = 0x40;
    private const uint CoverOffsetEntry = 201;
    private const uint NoIndex = 0xFFFFFFFF;

    /// <summary>The cover's bytes, or null when the file isn't a MOBI book, declares no cover, or the cover isn't an image.</summary>
    public static byte[]? Read(Stream stream)
    {
        var header = ReadAt(stream, 0, PdbHeaderLength);
        if (header is null || !header.AsSpan(60, 8).SequenceEqual("BOOKMOBI"u8))
        {
            return null;
        }

        var recordCount = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(RecordCountOffset));
        var recordList = ReadAt(stream, PdbHeaderLength, recordCount * 8);
        if (recordCount == 0 || recordList is null)
        {
            return null;
        }

        long RecordStart(int index) => BinaryPrimitives.ReadUInt32BigEndian(recordList.AsSpan(index * 8));
        long RecordEnd(int index) => index + 1 < recordCount ? RecordStart(index + 1) : stream.Length;

        var record0 = ReadRecord(stream, RecordStart(0), RecordEnd(0));
        if (record0 is null || record0.Length < PalmDocHeaderLength + ExthFlagsOffset + 4
            || !record0.AsSpan(PalmDocHeaderLength, 4).SequenceEqual("MOBI"u8))
        {
            return null;
        }

        var mobi = record0.AsSpan(PalmDocHeaderLength);
        var mobiHeaderLength = BinaryPrimitives.ReadUInt32BigEndian(mobi[MobiHeaderLengthOffset..]);
        var firstImage = BinaryPrimitives.ReadUInt32BigEndian(mobi[FirstImageIndexOffset..]);
        var exthFlags = BinaryPrimitives.ReadUInt32BigEndian(mobi[ExthFlagsOffset..]);
        if (firstImage == NoIndex || (exthFlags & ExthPresent) == 0 || mobiHeaderLength >= mobi.Length
            || CoverOffset(mobi[(int)mobiHeaderLength..]) is not { } coverOffset)
        {
            return null;
        }

        var coverRecord = (long)firstImage + coverOffset;
        if (coverRecord >= recordCount)
        {
            return null;
        }

        var (start, end) = (RecordStart((int)coverRecord), RecordEnd((int)coverRecord));
        var cover = end - start <= BookCoverExtractor.MaxCoverBytes ? ReadRecord(stream, start, end) : null;
        return cover is { Length: > 0 } && BookCoverExtractor.SniffImageType(cover) is not null
            ? cover
            : null;
    }

    /// <summary>EXTH entry 201, the cover's place among the book's images; null when the section has none.</summary>
    private static uint? CoverOffset(ReadOnlySpan<byte> exth)
    {
        if (exth.Length < 12 || !exth[..4].SequenceEqual("EXTH"u8))
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(exth[8..]);
        var position = 12;
        for (var i = 0; i < count && position + 8 <= exth.Length; i++)
        {
            var type = BinaryPrimitives.ReadUInt32BigEndian(exth[position..]);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(exth[(position + 4)..]);
            if (length < 8 || position + length > exth.Length)
            {
                return null;
            }

            if (type == CoverOffsetEntry && length >= 12)
            {
                var offset = BinaryPrimitives.ReadUInt32BigEndian(exth[(position + 8)..]);
                return offset == NoIndex ? null : offset;
            }

            position += length;
        }

        return null;
    }

    private static byte[]? ReadRecord(Stream stream, long start, long end) =>
        start < end && end <= stream.Length && end - start <= int.MaxValue ? ReadAt(stream, start, (int)(end - start)) : null;

    private static byte[]? ReadAt(Stream stream, long position, int length)
    {
        if (position + length > stream.Length)
        {
            return null;
        }

        stream.Position = position;
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
