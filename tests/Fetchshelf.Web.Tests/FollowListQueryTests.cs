using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Entities;
using Fetchshelf.Web.Pages.Following;

namespace Fetchshelf.Web.Tests;

/// <summary>The Following page's and the lists of finds' filters and sort.</summary>
public class FollowListQueryTests
{
    private static FollowEntity Follow(string name, FollowKind kind = FollowKind.Author, FollowMode mode = FollowMode.Watch) => new()
    {
        Kind = kind,
        NameKey = name.ToLowerInvariant(),
        DisplayName = name,
        Mode = mode,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static FindRow Row(string title, string? book = null, long? size = null, int foundDay = 1) => new(
        new FollowFindEntity
        {
            FollowId = 1,
            MamId = 1,
            Title = title,
            SeriesDisplay = book,
            SizeBytes = size,
            MediaSide = "Audio",
            Status = FollowFindStatus.New,
            FoundAt = new DateTimeOffset(2026, 11, foundDay, 0, 0, 0, TimeSpan.Zero),
            StatusAt = DateTimeOffset.UnixEpoch,
        },
        Follow("Someone"));

    [Theory]
    [InlineData("5", 5.0)]
    [InlineData("5-7", 5.0)]
    [InlineData("2.5", 2.5)]
    [InlineData(" 10 ", 10.0)]
    [InlineData("Prequel", null)]
    [InlineData(null, null)]
    public void BookNumber_IsTheNumberTheEntryStartsWith(string? display, double? number) =>
        Assert.Equal(number, FindListQuery.BookNumber(display));

    [Fact]
    public void Finds_SortByBookNumber_WithUnnumberedOnesLast_EitherWay()
    {
        var rows = new[] { Row("Ten", "10"), Row("None"), Row("Two", "2") };
        var query = new FindListQuery(FindShow.ToLookAt, null, "Book", "asc", 25);

        Assert.Equal(["Two", "Ten", "None"], query.Order(rows).Select(r => r.Find.Title));
        Assert.Equal(["Ten", "Two", "None"], (query with { Dir = "desc" }).Order(rows).Select(r => r.Find.Title));
    }

    [Fact]
    public void Finds_SortNewestFoundFirst_ByDefault()
    {
        var rows = new[] { Row("Old", foundDay: 1), Row("New", foundDay: 9) };

        Assert.Equal(["New", "Old"], new FindListQuery(FindShow.ToLookAt, null, FindListQuery.DefaultSort, "desc", 25).Order(rows).Select(r => r.Find.Title));
    }

    [Fact]
    public void SortedBy_StartsInTheColumnsNaturalOrder_AndReversesASecondTime()
    {
        var query = new FindListQuery(FindShow.ToLookAt, null, FindListQuery.DefaultSort, "desc", 25);

        Assert.Equal(("Title", "asc"), (query.SortedBy("Title").Sort, query.SortedBy("Title").Dir));
        Assert.Equal("desc", query.SortedBy("Title").SortedBy("Title").Dir);
        Assert.Equal("desc", query.SortedBy("Size").Dir);
        Assert.Equal("asc", query.SortedBy("Found").Dir);
    }

    [Fact]
    public void FindRouteValues_LeaveOutTheDefaultShow()
    {
        var values = new FindListQuery(FindShow.ToLookAt, null, "Found", "desc", 25).ToRouteValues();

        Assert.False(values.ContainsKey("Show"));
        Assert.Equal("Dismissed", (new FindListQuery(FindShow.Dismissed, null, "Found", "desc", 25)).ToRouteValues()["Show"]);
    }

    [Fact]
    public void Follows_AreMatchedByEveryWordOfTheSearch_AndTheFilters()
    {
        var summaries = new[]
        {
            new FollowSummary(Follow("James S. A. Corey"), 2, 0, 0, 0),
            new FollowSummary(Follow("The Expanse", FollowKind.Series, FollowMode.Download), 0, 0, 1, 0),
            new FollowSummary(Follow("James Islington"), 0, 0, 0, 3),
        };
        var query = new FollowListQuery(null, null, null, false, FollowListQuery.DefaultSort, "asc", 25);

        Assert.Equal(["James Islington", "James S. A. Corey", "The Expanse"], query.Apply(summaries).Select(s => s.Follow.DisplayName));
        Assert.Equal(["James S. A. Corey"], (query with { Q = "corey james" }).Apply(summaries).Select(s => s.Follow.DisplayName));
        Assert.Equal(["The Expanse"], (query with { Kind = FollowKind.Series }).Apply(summaries).Select(s => s.Follow.DisplayName));
        Assert.Equal(["The Expanse"], (query with { Mode = FollowMode.Download }).Apply(summaries).Select(s => s.Follow.DisplayName));
        Assert.Equal(["James S. A. Corey"], (query with { Waiting = true }).Apply(summaries).Select(s => s.Follow.DisplayName));
        Assert.Equal("James S. A. Corey", query.SortedBy("ToLookAt").Apply(summaries).First().Follow.DisplayName);
    }
}
