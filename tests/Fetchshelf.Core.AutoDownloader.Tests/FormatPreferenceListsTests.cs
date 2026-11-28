using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader.Tests;

public class FormatPreferenceListsTests
{
    private static readonly FormatPreferenceLists Lists = new()
    {
        AudioTypes = ["m4b"],
        EbookTypes = ["epub"],
        MusicTypes = ["flac"],
        RadioTypes = ["mp3"],
    };

    [Theory]
    [InlineData(MediaType.Audiobook, "m4b")]
    [InlineData(MediaType.PeriodicalAudiobook, "m4b")]
    [InlineData(MediaType.Ebook, "m4b")]
    [InlineData(MediaType.ComicBook, "m4b")]
    [InlineData(MediaType.Musicology, "flac")]
    [InlineData(MediaType.Radio, "mp3")]
    public void AudioSideFor_MediaType_UsesThatTypesOwnListOrAudioTypes(MediaType mediaType, string expected)
    {
        Assert.Equal([expected], Lists.AudioSideFor(mediaType));
    }

    [Fact]
    public void SettingNameFor_EveryMediaType_NamesTheListThatForReturns()
    {
        var lists = new FormatPreferenceLists { AudioTypes = ["audio"], EbookTypes = ["ebook"], MusicTypes = ["music"], RadioTypes = ["radio"] };

        var withComics = lists with { ComicTypes = ["comic"] };
        foreach (var mediaType in Enum.GetValues<MediaType>())
        {
            Assert.Equal($"{lists.For(mediaType)[0]}_types", lists.SettingNameFor(mediaType));
            Assert.Equal($"{withComics.For(mediaType)[0]}_types", withComics.SettingNameFor(mediaType));
        }
    }

    [Theory]
    [InlineData(MediaType.ComicBook, "comic")]
    [InlineData(MediaType.Manga, "comic")]
    [InlineData(MediaType.Ebook, "ebook")]
    [InlineData(MediaType.PeriodicalEbook, "ebook")]
    public void For_WithComicTypes_GivesComicsAndMangaTheirOwnList(MediaType mediaType, string expected)
    {
        var lists = new FormatPreferenceLists { AudioTypes = ["audio"], EbookTypes = ["ebook"], MusicTypes = ["music"], RadioTypes = ["radio"], ComicTypes = ["comic"] };

        Assert.Equal([expected], lists.For(mediaType));
    }

    [Theory]
    // Comics: the library's comic_types, the global one, the library's ebook_types, the global one.
    [InlineData(MediaType.ComicBook, true, true, true, "library-comic")]
    [InlineData(MediaType.Manga, false, true, true, "global-comic")]
    [InlineData(MediaType.ComicBook, false, false, true, "library-ebook")]
    [InlineData(MediaType.ComicBook, false, false, false, "epub")]
    // Other ebooks never use a comic list.
    [InlineData(MediaType.Ebook, true, true, true, "library-ebook")]
    [InlineData(MediaType.Ebook, true, true, false, "epub")]
    public void EbookSideFor_TakesTheMostSpecificListThatIsSet(
        MediaType mediaType, bool libraryComic, bool globalComic, bool libraryEbook, string expected)
    {
        var lists = Lists with { ComicTypes = globalComic ? ["global-comic"] : null };

        var list = lists.EbookSideFor(mediaType, libraryEbook ? ["library-ebook"] : null, libraryComic ? ["library-comic"] : null);

        Assert.Equal([expected], list);
    }

    [Theory]
    [InlineData(MediaType.Ebook, "epub")]
    [InlineData(MediaType.PeriodicalEbook, "epub")]
    [InlineData(MediaType.ComicBook, "cbz")]
    [InlineData(MediaType.Audiobook, "m4b")]
    [InlineData(MediaType.Musicology, "flac")]
    public void ForLibrary_TakesTheLibrarysOwnList_WhereItHasOne(MediaType mediaType, string first)
    {
        var lists = new FormatPreferenceLists
        {
            AudioTypes = ["mp3"],
            EbookTypes = ["pdf"],
            MusicTypes = ["flac"],
            RadioTypes = ["mp3"],
        };
        var library = new LibraryFormatLists(["m4b"], ["epub"], ["cbz"]);

        Assert.Equal(first, lists.ForLibrary(mediaType, library)[0]);
        Assert.Equal(lists.For(mediaType), lists.ForLibrary(mediaType, null));
    }
}
