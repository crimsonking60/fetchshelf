using Fetchshelf.Core.Metadata;
using Fetchshelf.Web.Pages.Torrents;

namespace Fetchshelf.Web.Tests;

public class TorrentSearchMatcherTests
{
    private static TorrentMetadata Book(string title, string[] authors, string? series = null, string? description = null) =>
        OrganizerTestSupport.BuildMetadata(title, authors) with
        {
            Series = series is null ? [] : [new Series { Name = series, Entries = [] }],
            Description = description,
        };

    [Theory]
    [InlineData("belle")]
    [InlineData("BELLE DE JOUR")]
    [InlineData("adventures")]
    [InlineData("call girl belle")]
    public void Matches_DifferentCaseAndWordOrder_StillMatches(string query) =>
        Assert.True(TorrentSearchMatcher.Matches(
            Book("The Further Adventures of a London Call Girl", ["Belle De Jour"], series: "Belle De Jour"), query));

    [Theory]
    [InlineData("beautiful american")]
    [InlineData("american beautiful")]
    [InlineData("mackin american")]
    public void Matches_WordsSpreadOverTitleAndAuthor_MatchesInAnyOrder(string query) =>
        Assert.True(TorrentSearchMatcher.Matches(Book("The Beautiful American", ["Jeanne Mackin"]), query));

    [Fact]
    public void Matches_AccentedText_MatchesWithoutAccents() =>
        Assert.True(TorrentSearchMatcher.Matches(Book("Jane Eyre", ["Charlotte Brontë"]), "bronte"));

    [Fact]
    public void Matches_AWordFoundNowhere_IsNoMatch() =>
        Assert.False(TorrentSearchMatcher.Matches(Book("The Beautiful American", ["Jeanne Mackin"]), "beautiful french"));

    [Fact]
    public void Matches_TextOnlyInTheDescription_IsNoMatch() =>
        Assert.False(TorrentSearchMatcher.Matches(
            Book("The Way of Kings", ["Brandon Sanderson"], description: "Further adventures await."), "adventures"));
}
