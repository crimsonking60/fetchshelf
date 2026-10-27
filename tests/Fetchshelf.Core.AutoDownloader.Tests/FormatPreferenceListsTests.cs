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
}
