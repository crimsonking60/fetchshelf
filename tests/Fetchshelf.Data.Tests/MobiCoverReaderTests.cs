using System.Buffers.Binary;
using Fetchshelf.Data.Opds;

namespace Fetchshelf.Data.Tests;

public sealed class MobiCoverReaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-mobi-cover-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 5, 6];

    /// <summary>
    /// A Palm database as a MOBI book is laid out: record 0 with the PalmDOC and MOBI headers and an EXTH section, a text
    /// record, then the images. The cover is the image at <paramref name="coverOffset"/> among them (EXTH 201), left out
    /// when null; <paramref name="exth"/> false leaves the EXTH section out.
    /// </summary>
    internal static byte[] Mobi(IReadOnlyList<byte[]> images, uint? coverOffset, bool exth = true, string creator = "BOOKMOBI")
    {
        const int mobiHeaderLength = 232;
        var exthBytes = new List<byte>();
        if (exth)
        {
            var entries = new List<byte[]>();
            entries.Add(Entry(100, "Ann Author"u8.ToArray()));
            if (coverOffset is { } offset)
            {
                entries.Add(Entry(201, BigEndian(offset)));
            }

            exthBytes.AddRange("EXTH"u8.ToArray());
            exthBytes.AddRange(BigEndian((uint)(12 + entries.Sum(e => e.Length))));
            exthBytes.AddRange(BigEndian((uint)entries.Count));
            foreach (var entry in entries)
            {
                exthBytes.AddRange(entry);
            }
        }

        var record0 = new byte[16 + mobiHeaderLength + exthBytes.Count];
        "MOBI"u8.CopyTo(record0.AsSpan(16));
        BinaryPrimitives.WriteUInt32BigEndian(record0.AsSpan(20), mobiHeaderLength);
        BinaryPrimitives.WriteUInt32BigEndian(record0.AsSpan(16 + 92), 2); // the first image is record 2
        BinaryPrimitives.WriteUInt32BigEndian(record0.AsSpan(16 + 112), exth ? 0x50u : 0x10u);
        exthBytes.CopyTo(record0, 16 + mobiHeaderLength);

        var records = new List<byte[]> { record0, "Some text."u8.ToArray() };
        records.AddRange(images);

        var header = new byte[78 + (records.Count * 8) + 2];
        "Book"u8.CopyTo(header);
        System.Text.Encoding.ASCII.GetBytes(creator).CopyTo(header, 60);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(76), (ushort)records.Count);
        var position = header.Length;
        for (var i = 0; i < records.Count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(78 + (i * 8)), (uint)position);
            position += records[i].Length;
        }

        return [.. header, .. records.SelectMany(r => r)];

        static byte[] Entry(uint type, byte[] data) => [.. BigEndian(type), .. BigEndian((uint)(8 + data.Length)), .. data];
        static byte[] BigEndian(uint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            return bytes;
        }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData("book.azw3")]
    [InlineData("book.azw")]
    [InlineData("book.mobi")]
    public void Extract_TheImageExth201Names(string name)
    {
        var path = Write(name, Mobi([BookCoverExtractorTests.Jpeg, Png], coverOffset: 1));

        var cover = BookCoverExtractor.Extract(path);

        Assert.Equal(Png, cover!.Bytes);
        Assert.Equal("image/png", cover.ContentType);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(5u, true)]
    [InlineData(0xFFFFFFFFu, true)]
    [InlineData(0u, false)]
    public void Read_NoCoverDeclared_OrOneThatIsntThere_IsNull(uint? coverOffset, bool exth)
    {
        using var stream = new MemoryStream(Mobi([BookCoverExtractorTests.Jpeg], coverOffset, exth));

        Assert.Null(MobiCoverReader.Read(stream));
    }

    [Fact]
    public void Read_ACoverRecordThatIsntAnImage_IsNull()
    {
        using var stream = new MemoryStream(Mobi(["FONT and more"u8.ToArray()], coverOffset: 0));

        Assert.Null(MobiCoverReader.Read(stream));
    }

    [Theory]
    [InlineData("TEXtREAd")]
    public void Read_APalmDatabaseThatIsntAMobiBook_IsNull(string creator)
    {
        using var stream = new MemoryStream(Mobi([BookCoverExtractorTests.Jpeg], coverOffset: 0, creator: creator));

        Assert.Null(MobiCoverReader.Read(stream));
    }

    [Fact]
    public void Read_ATruncatedFile_IsNull()
    {
        var bytes = Mobi([BookCoverExtractorTests.Jpeg], coverOffset: 0);
        using var stream = new MemoryStream(bytes[..100]);

        Assert.Null(MobiCoverReader.Read(stream));
    }
}
