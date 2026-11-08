using System.Xml.Linq;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class LibraryMetadataOpfBuilderTests
{
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    private static TorrentMetadata Build() => new()
    {
        MamId = 12345,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.Audiobook,
        LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
        GenreCategories = new HashSet<GenreCategory> { GenreCategory.Fantasy },
        Language = Language.English,
        Title = "The Way of Kings: A Novel",
        Authors = ["Brandon Sanderson"],
        Narrators = ["Michael Kramer", "Kate Reading"],
        Series = [new Series { Name = "Stormlight Archive", Entries = [SeriesEntry.ForNumber(1, "1")] }],
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
        Description = "An epic fantasy novel.",
        Isbn = "9780765326355",
        Asin = "B003P2WO5E",
    };

    private static XElement Metadata(TorrentMetadata metadata) =>
        XDocument.Parse(LibraryMetadataOpfBuilder.Build(metadata)).Root!.Element(Opf + "metadata")!;

    private static List<(string Name, string Content)> Metas(XElement metadata) =>
        metadata.Elements(Opf + "meta").Select(m => ((string)m.Attribute("name")!, (string)m.Attribute("content")!)).ToList();

    [Fact]
    public void Build_FullMetadata_WritesEveryField()
    {
        var metadata = Metadata(Build());

        Assert.Equal("The Way of Kings: A Novel", (string?)metadata.Element(Dc + "title"));
        Assert.Equal(
            [("aut", "Brandon Sanderson"), ("nrt", "Michael Kramer"), ("nrt", "Kate Reading")],
            metadata.Elements(Dc + "creator").Select(c => ((string)c.Attribute(Opf + "role")!, c.Value)));
        Assert.Equal("An epic fantasy novel.", (string?)metadata.Element(Dc + "description"));
        Assert.Equal("eng", (string?)metadata.Element(Dc + "language"));
        Assert.Equal(
            [("MAM", "12345"), ("ISBN", "9780765326355"), ("ASIN", "B003P2WO5E")],
            metadata.Elements(Dc + "identifier").Select(i => ((string)i.Attribute(Opf + "scheme")!, i.Value)));
        Assert.Equal(["General Fiction", "Fantasy"], metadata.Elements(Dc + "subject").Select(s => s.Value));
        Assert.Equal([("calibre:series", "Stormlight Archive"), ("calibre:series_index", "1")], Metas(metadata));
    }

    [Fact]
    public void Build_TextXmlCannotHold_IsLeftOut()
    {
        var metadata = Metadata(Build() with { Title = "The\u0001 Way of Kings", Description = "Page one.\fPage two." });

        Assert.Equal("The Way of Kings", (string?)metadata.Element(Dc + "title"));
        Assert.Equal("Page one.Page two.", (string?)metadata.Element(Dc + "description"));
    }

    [Fact]
    public void Build_PackageIdentifiesTheBookByItsMamId()
    {
        var package = XDocument.Parse(LibraryMetadataOpfBuilder.Build(Build())).Root!;

        Assert.Equal(Opf + "package", package.Name);
        Assert.Equal("2.0", (string?)package.Attribute("version"));
        var id = (string)package.Attribute("unique-identifier")!;
        Assert.Equal("12345", package.Descendants(Dc + "identifier").Single(i => (string?)i.Attribute("id") == id).Value);
    }

    [Fact]
    public void Build_WritesOpfElementsWithoutAPrefix_AsCalibreDoes()
    {
        var opf = LibraryMetadataOpfBuilder.Build(Build());

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", opf);
        Assert.Contains("<package ", opf);
        Assert.Contains("<metadata ", opf);
        Assert.Contains("<meta name=\"calibre:series\"", opf);
        Assert.DoesNotContain("<opf:", opf);
    }

    [Fact]
    public void Build_MinimalMetadata_LeavesOutWhatIsUnknown()
    {
        var metadata = Metadata(Build() with
        {
            Narrators = [],
            Series = [],
            Description = null,
            Isbn = null,
            Asin = null,
            Language = Language.Other,
        });

        Assert.Single(metadata.Elements(Dc + "creator"));
        Assert.Null(metadata.Element(Dc + "description"));
        Assert.Null(metadata.Element(Dc + "language"));
        Assert.Equal(["MAM"], metadata.Elements(Dc + "identifier").Select(i => (string)i.Attribute(Opf + "scheme")!));
        Assert.Empty(Metas(metadata));
    }

    [Fact]
    public void Build_SeveralSeries_WritesEachWithItsFirstNumber()
    {
        var metadata = Metadata(Build() with
        {
            Series =
            [
                new Series { Name = "Cosmere", Entries = [SeriesEntry.ForNumber(3.5m, "3.5"), SeriesEntry.ForNumber(2, "2")] },
                new Series { Name = "Unnumbered", Entries = [] },
            ],
        });

        Assert.Equal(
            [("calibre:series", "Cosmere"), ("calibre:series_index", "2"), ("calibre:series", "Unnumbered")],
            Metas(metadata));
    }

    [Fact]
    public void Build_TextWithXmlCharacters_IsEscaped()
    {
        var metadata = Metadata(Build() with { Title = "Salt & <Pepper>", Description = "\"Quoted\" & more" });

        Assert.Equal("Salt & <Pepper>", (string?)metadata.Element(Dc + "title"));
        Assert.Equal("\"Quoted\" & more", (string?)metadata.Element(Dc + "description"));
    }
}
