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
