using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class SameBookComparerTests
{
    private static TorrentMetadata Build(
        MediaType mediaType = MediaType.Audiobook,
        Language language = Language.English,
        Edition? edition = null,
        IReadOnlyList<string>? authors = null,
        IReadOnlyList<string>? narrators = null) => new()
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = mediaType,
            LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
            Language = language,
            Title = "Some Title",
            Edition = edition,
            Authors = authors ?? ["Jane Doe"],
            Narrators = narrators ?? [],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UtcNow,
        };

    [Fact]
    public void AreSameBook_EbookAndComicBook_AreInterchangeable()
    {
        var a = Build(mediaType: MediaType.Ebook);
        var b = Build(mediaType: MediaType.ComicBook);

        Assert.True(SameBookComparer.AreSameBook(a, b));
    }

    [Fact]
    public void AreSameBook_DifferentEditionNumbers_AreNotSameBook()
    {
        var a = Build(edition: new Edition("2nd Edition", 2));
        var b = Build(edition: new Edition("3rd Edition", 3));

        Assert.False(SameBookComparer.AreSameBook(a, b));
    }

    [Fact]
    public void AreSameBook_OnlyOneHasEdition_AreNotSameBook()
    {
        var a = Build(edition: new Edition("2nd Edition", 2));
        var b = Build(edition: null);

        Assert.False(SameBookComparer.AreSameBook(a, b));
    }

    [Fact]
    public void AreSameBook_EditionNumberZeroWithDifferentLabels_ReturnsFalse()
    {
        var a = Build(edition: new Edition("UK Edition", 0));
        var b = Build(edition: new Edition("US Edition", 0));

        Assert.False(SameBookComparer.AreSameBook(a, b));
    }

    [Fact]
    public void AreSameBook_EditionNumberZeroWithMatchingLabel_AreSameBook()
    {
        var a = Build(edition: new Edition("UK Edition", 0));
        var b = Build(edition: new Edition("UK Edition", 0));

        Assert.True(SameBookComparer.AreSameBook(a, b));
    }

    [Fact]
    public void AreSameBook_DifferentLanguages_AreNotSameBook()
    {
        var a = Build(language: Language.English);
        var b = Build(language: Language.French);

        Assert.False(SameBookComparer.AreSameBook(a, b));
    }

    [Fact]
    public void AreSameBook_NoSharedAuthor_AreNotSameBook()
    {
        var a = Build(authors: ["Jane Doe"]);
        var b = Build(authors: ["John Smith"]);

        Assert.False(SameBookComparer.AreSameBook(a, b));
    }

    [Fact]
    public void AreSameBook_BothHaveNoNarrators_AreSameBook()
    {
        var a = Build(narrators: []);
        var b = Build(narrators: []);

        Assert.True(SameBookComparer.AreSameBook(a, b));
    }

    [Fact]
    public void AreSameBook_SharedNarrator_AreSameBook()
    {
        var a = Build(narrators: ["Narrator One", "Narrator Two"]);
        var b = Build(narrators: ["Narrator Two"]);

        Assert.True(SameBookComparer.AreSameBook(a, b));
    }

    [Fact]
    public void AreSameBook_OneHasNarratorOtherDoesNot_AreNotSameBook()
    {
        var a = Build(narrators: ["Narrator One"]);
        var b = Build(narrators: []);

        Assert.False(SameBookComparer.AreSameBook(a, b));
    }

    [Theory]
    [InlineData("J. R. R. Tolkien", "J.R.R. Tolkien")]
    [InlineData("Ursula K. Le Guin", "ursula k. leguin")]
    public void AreSameBook_AuthorsSpelledDifferently_StillShareTheAuthor(string a, string b)
    {
        Assert.True(SameBookComparer.AreSameBook(Build(authors: [a]), Build(authors: [b])));
    }

    [Fact]
    public void AreSameBook_NarratorsSpelledDifferently_StillAgree()
    {
        Assert.True(SameBookComparer.AreSameBook(Build(narrators: ["Michael Kramer"]), Build(narrators: ["michael  kramer"])));
    }

    [Fact]
    public void AreSameBook_AuthorsWithNoLettersAtAll_DontMatch()
    {
        Assert.False(SameBookComparer.AreSameBook(Build(authors: ["?"]), Build(authors: ["?"])));
    }
}
