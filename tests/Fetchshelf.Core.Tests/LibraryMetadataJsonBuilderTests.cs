using System.Text.Json;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class LibraryMetadataJsonBuilderTests
{
    private static TorrentMetadata Build() => new()
    {
        MamId = 1,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.Audiobook,
        LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
        GenreCategories = new HashSet<GenreCategory> { GenreCategory.Fantasy },
        Language = Language.English,
        Flags = BookFlags.Lgbt | BookFlags.Explicit | BookFlags.Abridged,
        Title = "The Way of Kings: A Novel",
        Authors = ["Brandon Sanderson"],
        Narrators = ["Michael Kramer"],
        Series = [new Series { Name = "Stormlight Archive", Entries = [SeriesEntry.ForNumber(1, "1")] }],
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
        Description = "An epic fantasy novel.",
        Isbn = "9780765326355",
    };

    [Fact]
    public void Build_FullMetadata_WritesEveryDocumentedField()
    {
        var json = LibraryMetadataJsonBuilder.Build(Build());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("Brandon Sanderson", root.GetProperty("authors")[0].GetString());
        Assert.Equal("Michael Kramer", root.GetProperty("narrators")[0].GetString());
        Assert.Equal("Stormlight Archive #1", root.GetProperty("series")[0].GetString());
        Assert.Equal("The Way of Kings", root.GetProperty("title").GetString());
        Assert.Equal("A Novel", root.GetProperty("subtitle").GetString());
        Assert.Equal("An epic fantasy novel.", root.GetProperty("description").GetString());
        Assert.Equal("9780765326355", root.GetProperty("isbn").GetString());
        Assert.Equal("LGBT", root.GetProperty("tags")[0].GetString());
        Assert.Contains("Fantasy", root.GetProperty("genres").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("General Fiction", root.GetProperty("genres").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("English", root.GetProperty("language").GetString());
        Assert.True(root.GetProperty("explicit").GetBoolean());
        Assert.True(root.GetProperty("abridged").GetBoolean());
    }

    [Fact]
    public void Build_LanguageOfMoreThanOneWord_IsWrittenAsItsName()
    {
        var json = LibraryMetadataJsonBuilder.Build(Build() with { Language = Language.BrazilianPortuguese });
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Brazilian Portuguese", document.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public void Build_TitleWithShortPrefixBeforeColon_DoesNotSplitSubtitle()
    {
        var metadata = Build() with { Title = "Vol: The Beginning" };

        var json = LibraryMetadataJsonBuilder.Build(metadata);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Vol: The Beginning", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("subtitle").ValueKind);
    }

    [Fact]
    public void BuildGenres_WithMamGenreNames_UsesMamsNames_AndAddsTheMainCategory()
    {
        var metadata = Build() with
        {
            FictionCategory = FictionCategory.Nonfiction,
            GenreCategories = new HashSet<GenreCategory> { GenreCategory.Politics, GenreCategory.LickLibraryLtp },
        };

        // Without: the short names, as always, and no main category.
        Assert.Equal(["Erotica", "General Fiction", "Politics"], LibraryMetadataJsonBuilder.BuildGenres(metadata).Order());
        // With: the old category first, then the main category, then MAM's names.
        var named = LibraryMetadataJsonBuilder.BuildGenres(metadata, mamGenreNames: true);
        Assert.Equal(["General Fiction", "Nonfiction"], named.Take(2));
        Assert.Equal(["Erotica/Sexual Content", "Politics/Sociology"], named.Skip(2).Order());
        using var document = JsonDocument.Parse(LibraryMetadataJsonBuilder.Build(metadata, mamGenreNames: true));
        Assert.Contains("Politics/Sociology", document.RootElement.GetProperty("genres").EnumerateArray().Select(e => e.GetString()));
    }
}
