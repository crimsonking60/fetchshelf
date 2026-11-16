using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader.Tests;

public class ListCandidateRankerTests
{
    private static ListCandidate<string> Build(
        string payload, string title, IReadOnlyList<string> authors, IReadOnlyList<string> fileTypes, int fileCount = 1, long sizeBytes = 100) => new()
        {
            Metadata = new TorrentMetadata
            {
                MamId = 1,
                VipStatus = VipStatus.NotVip,
                MediaType = MediaType.Audiobook,
                LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
                Language = Language.English,
                Title = title,
                Authors = authors,
                FileTypes = fileTypes,
                FileCount = fileCount,
                TotalSizeBytes = sizeBytes,
                MetadataSource = MetadataSource.Mam,
                UploadTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
            Payload = payload,
        };

    private static ListCandidate<string> WithIds(ListCandidate<string> candidate, string? isbn = null, string? asin = null) =>
        candidate with { Metadata = candidate.Metadata with { Isbn = isbn, Asin = asin } };

    [Fact]
    public void IdentifierMatches_AnIsbn13Match_BeatsABetterTitleMatch()
    {
        // A translated or subtitled upload: its title is far off, its ISBN is the book's.
        var byIsbn = WithIds(Build("by-isbn", "Der Weg der Könige", ["Brandon Sanderson"], ["m4b"]), isbn: "978-0-7653-2635-5");
        var byTitle = Build("by-title", "The Way of Kings", ["Brandon Sanderson"], ["m4b"]);

        var matches = ListCandidateRanker.IdentifierMatches([byTitle, byIsbn], BookIdentifiers.From(["9780765326355"]), ["m4b"]);

        Assert.Equal(["by-isbn"], matches.Select(m => m.Payload));
    }

    [Fact]
    public void IdentifierMatches_AnIsbn10OnTheUpload_MatchesTheBooksIsbn13()
    {
        var candidate = WithIds(Build("isbn10", "The Way of Kings", ["Brandon Sanderson"], ["m4b"]), isbn: "0765326353");

        Assert.Single(ListCandidateRanker.IdentifierMatches([candidate], BookIdentifiers.From(["9780765326355"]), ["m4b"]));
    }

    [Fact]
    public void IdentifierMatches_AnAsin_Matches_AndTheBestFormatComesFirst()
    {
        var mp3 = WithIds(Build("mp3", "The Way of Kings", ["Brandon Sanderson"], ["mp3"]), asin: "B003P2WO5E");
        var m4b = WithIds(Build("m4b", "The Way of Kings", ["Brandon Sanderson"], ["m4b"]), asin: " b003p2wo5e");

        var matches = ListCandidateRanker.IdentifierMatches([mp3, m4b], BookIdentifiers.From(["B003P2WO5E"]), ["m4b", "mp3"]);

        Assert.Equal(["m4b", "mp3"], matches.Select(m => m.Payload));
    }

    [Fact]
    public void IdentifierMatches_NoIdentifiersOrNoMatch_IsEmpty_SoTheFuzzyRankingDecides()
    {
        var candidate = WithIds(Build("other", "The Way of Kings", ["Brandon Sanderson"], ["m4b"]), isbn: "9780306406157");

        Assert.Empty(ListCandidateRanker.IdentifierMatches([candidate], BookIdentifiers.From([]), ["m4b"]));
        Assert.Empty(ListCandidateRanker.IdentifierMatches([candidate], BookIdentifiers.From(["9780765326355"]), ["m4b"]));
        Assert.Empty(ListCandidateRanker.IdentifierMatches([Build("none", "The Way of Kings", ["Brandon Sanderson"], ["m4b"])], BookIdentifiers.From(["9780765326355"]), ["m4b"]));
    }

    [Fact]
    public void PreferredFiletype_RankedAboveNonPreferredAtSimilarScore()
    {
        var preferred = Build("preferred", "Mistborn", ["Brandon Sanderson"], ["m4b"]);
        var nonPreferred = Build("non-preferred", "Mistborn", ["Brandon Sanderson"], ["mp3"]);

        var ranked = ListCandidateRanker.RankAndFilter(
            [nonPreferred, preferred], "Mistborn", ["Brandon Sanderson"], series: null, preferredFileTypes: ["m4b", "mp3"]);

        Assert.Equal("preferred", ranked[0].Payload);
    }

    [Fact]
    public void BelowMarginCandidate_IsExcluded()
    {
        var bestMatch = Build("best", "Mistborn", ["Brandon Sanderson"], ["m4b"]);
        var noMatch = Build("no-match", "A Completely Unrelated Title", ["Someone Else Entirely"], ["m4b"]);

        var ranked = ListCandidateRanker.RankAndFilter(
            [bestMatch, noMatch], "Mistborn", ["Brandon Sanderson"], series: null, preferredFileTypes: ["m4b"]);

        Assert.Single(ranked);
        Assert.Equal("best", ranked[0].Payload);
    }

    [Fact]
    public void NoCandidates_ReturnsEmpty()
    {
        var ranked = ListCandidateRanker.RankAndFilter<string>(
            [], "Mistborn", ["Brandon Sanderson"], series: null, preferredFileTypes: ["m4b"]);

        Assert.Empty(ranked);
    }
}
