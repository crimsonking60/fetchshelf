using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader.Tests;

public class ListLibraryMatcherTests
{
    private static ListCandidate<string> Build(string payload, string title, IReadOnlyList<string> authors, MediaType mediaType = MediaType.Audiobook) => new()
    {
        Metadata = new TorrentMetadata
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = mediaType,
            LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
            Language = Language.English,
            Title = title,
            Authors = authors,
            MetadataSource = MetadataSource.Mam,
            UploadTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        },
        Payload = payload,
    };

    [Fact]
    public void ConfidentMatch_ReturnsCandidate()
    {
        var candidates = new[] { Build("match", "Mistborn", ["Brandon Sanderson"]) };

        var match = ListLibraryMatcher.FindMatch("Mistborn", ["Brandon Sanderson"], FilterMainType.Audio, candidates);

        Assert.NotNull(match);
        Assert.Equal("match", match.Payload);
    }

    [Fact]
    public void BookWithoutAuthors_MatchesOnTheTitleAlone()
    {
        var candidates = new[] { Build("match", "Mistborn", ["Brandon Sanderson"]) };

        var match = ListLibraryMatcher.FindMatch("Mistborn", [], FilterMainType.Audio, candidates);

        Assert.Equal("match", match?.Payload);
    }

    [Fact]
    public void BookWithoutAuthors_StillNeedsTheTitle()
    {
        var candidates = new[] { Build("no-match", "A Completely Different Book", ["Brandon Sanderson"]) };

        Assert.Null(ListLibraryMatcher.FindMatch("Mistborn", [], FilterMainType.Audio, candidates));
    }

    [Fact]
    public void TitleBelowThreshold_ReturnsNull()
    {
        var candidates = new[] { Build("no-match", "A Completely Different Book", ["Brandon Sanderson"]) };

        var match = ListLibraryMatcher.FindMatch("Mistborn", ["Brandon Sanderson"], FilterMainType.Audio, candidates);

        Assert.Null(match);
    }

    [Fact]
    public void AuthorBelowThreshold_ReturnsNull()
    {
        var candidates = new[] { Build("no-match", "Mistborn", ["A Totally Different Author"]) };

        var match = ListLibraryMatcher.FindMatch("Mistborn", ["Brandon Sanderson"], FilterMainType.Audio, candidates);

        Assert.Null(match);
    }

    [Fact]
    public void NoCandidates_ReturnsNull()
    {
        var match = ListLibraryMatcher.FindMatch<string>("Mistborn", ["Brandon Sanderson"], FilterMainType.Audio, []);

        Assert.Null(match);
    }

    [Fact]
    public void WrongMainType_IsSkipped()
    {
        var candidates = new[] { Build("no-match", "Mistborn", ["Brandon Sanderson"], MediaType.Ebook) };

        var match = ListLibraryMatcher.FindMatch("Mistborn", ["Brandon Sanderson"], FilterMainType.Audio, candidates);

        Assert.Null(match);
    }

    [Theory]
    [InlineData("R.T. de Vries", "R T de Vries")]
    [InlineData("R.T. de Vries", "R. T. de Vries")]
    [InlineData("Henry P. Dowd Jr.", "Henry P Dowd")]
    [InlineData("Henry P. Dowd, Jr.", "Henry P. Dowd Jr")]
    [InlineData("ann-marie quill", "Ann Marie Quill")]
    public void AnAuthorWrittenWithOtherPunctuationOrASuffix_Matches(string listAuthor, string mamAuthor)
    {
        var candidates = new[] { Build("match", "Slow Sky", [mamAuthor], MediaType.Ebook) };

        Assert.NotNull(ListLibraryMatcher.FindMatch("Slow Sky", [listAuthor], FilterMainType.Ebook, candidates));
    }

    [Fact]
    public void AnotherAuthorWithASimilarName_StillDoesntMatch()
    {
        var candidates = new[] { Build("other", "Slow Sky", ["R T de Vos"], MediaType.Ebook) };

        Assert.Null(ListLibraryMatcher.FindMatch("Slow Sky", ["R.T. de Vries"], FilterMainType.Ebook, candidates));
    }
}
