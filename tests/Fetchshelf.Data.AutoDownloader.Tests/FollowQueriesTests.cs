using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class FollowQueriesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-follow-queries-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly IReadOnlySet<string> EbooksOnly = new HashSet<string> { "Ebook" };

    private static GrabEntry Block(bool audio = true, bool ebook = true) => new()
    {
        Categories = new CategoriesConfig
        {
            Audio = audio ? CategoryListOrBool.All : CategoryListOrBool.None,
            Ebook = ebook ? CategoryListOrBool.All : CategoryListOrBool.None,
        },
    };

    /// <summary>
    /// Two followed authors and an ignored series. The first author has an ebook of every state, and a new and a
    /// not-free audiobook; the second one new ebook; the ignored series one new ebook.
    /// </summary>
    private async Task<(int First, int Second)> SeedAsync()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        FollowEntity Follow(string key, FollowKind kind, FollowMode mode) => new()
        {
            Kind = kind,
            NameKey = key,
            DisplayName = key,
            Mode = mode,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        var first = Follow("first", FollowKind.Author, FollowMode.Watch);
        var second = Follow("second", FollowKind.Author, FollowMode.Download);
        var ignored = Follow("ignored", FollowKind.Series, FollowMode.Ignore);
        context.Follows.AddRange(first, second, ignored);
        await context.SaveChangesAsync();
        var mamId = 0;
        void Find(FollowEntity follow, FollowFindStatus status, string side = "Ebook") => context.FollowFinds.Add(new FollowFindEntity
        {
            FollowId = follow.Id,
            MamId = ++mamId,
            Title = $"{follow.NameKey} {status} {side}",
            MediaSide = side,
            Status = status,
            FoundAt = DateTimeOffset.UnixEpoch,
            StatusAt = DateTimeOffset.UnixEpoch,
        });
        foreach (var status in Enum.GetValues<FollowFindStatus>())
        {
            Find(first, status);
        }

        Find(first, FollowFindStatus.New, "Audio");
        Find(first, FollowFindStatus.Wanted, "Audio");
        Find(second, FollowFindStatus.New);
        Find(ignored, FollowFindStatus.New);
        await context.SaveChangesAsync();
        return (first.Id, second.Id);
    }

    [Fact]
    public void WantedSides_AreTheFormatsAnyBlockTakes()
    {
        Assert.Equal(["Ebook"], FollowQueries.WantedSides([Block(audio: false)]).Order());
        Assert.Equal(["Audio", "Ebook"], FollowQueries.WantedSides([Block(audio: false), Block(ebook: false)]).Order());
        Assert.Equal(["Audio"], FollowQueries.WantedSides([new GrabEntry { Categories = new CategoriesConfig { Audio = CategoryListOrBool.Of(["Fantasy"]), Ebook = CategoryListOrBool.None } }]).Order());
    }

    [Fact]
    public void CanBeNotFree_OnlyWithAFreeBlock()
    {
        Assert.True(FollowQueries.CanBeNotFree([new GrabEntry { Cost = ConfiguredCost.Ratio }, new GrabEntry { Cost = ConfiguredCost.Free }]));
        Assert.False(FollowQueries.CanBeNotFree([new GrabEntry { Cost = ConfiguredCost.Ratio }, new GrabEntry { Cost = ConfiguredCost.Wedge }]));
    }

    [Fact]
    public async Task Summaries_CountEachFollowsFinds_LeavingOutIgnoredOnes_AndWaitingOnesInAFormatNoBlockTakes()
    {
        var (first, second) = await SeedAsync();
        using var context = SelectionTestSupport.CreateContext(DbPath);

        var summaries = await FollowQueries.SummariesAsync(context, EbooksOnly);

        Assert.Equal(["first", "second"], summaries.Select(s => s.Follow.NameKey).Order());
        var firstSummary = summaries.Single(s => s.Follow.Id == first);
        Assert.Equal((2, 1, 1, 1), (firstSummary.ToLookAt, firstSummary.NotFree, firstSummary.Selected, firstSummary.InLibrary));
        Assert.Equal(1, summaries.Single(s => s.Follow.Id == second).ToLookAt);
        // The menu's count is the same finds.
        Assert.Equal(summaries.Sum(s => s.ToLookAt), await FollowQueries.WaitingCountAsync(context, EbooksOnly));
    }

    [Theory]
    [InlineData(FindShow.ToLookAt, null, new[] { "first New Ebook", "first Wanted Ebook", "second New Ebook" })]
    [InlineData(FindShow.NotFree, null, new[] { "first Wanted Ebook" })]
    [InlineData(FindShow.Selected, null, new[] { "first Selected Ebook" })]
    [InlineData(FindShow.InLibrary, null, new[] { "first Owned Ebook" })]
    [InlineData(FindShow.Dismissed, null, new[] { "first Dismissed Ebook" })]
    [InlineData(FindShow.All, "Audio", new[] { "first New Audio", "first Wanted Audio" })]
    public async Task Finds_OfEveryFollow_ByWhatBecameOfThem(FindShow show, string? side, string[] titles)
    {
        await SeedAsync();
        using var context = SelectionTestSupport.CreateContext(DbPath);

        var rows = await FollowQueries.FindsAsync(context, null, show, side, EbooksOnly);

        Assert.Equal(titles, rows.Select(r => r.Find.Title).Order());
        Assert.All(rows, r => Assert.Equal(r.Find.FollowId, r.Follow.Id));
    }

    [Fact]
    public async Task ASelectedFindLinkedSince_IsInTheLibrary_BeforeASearchMarksIt()
    {
        var (first, _) = await SeedAsync();
        using (var seed = SelectionTestSupport.CreateContext(DbPath))
        {
            var mamId = seed.FollowFinds.Single(f => f.Title == "first Selected Ebook").MamId;
            seed.Torrents.Add(new TorrentEntity
            {
                Id = "hash1",
                MamId = mamId,
                Title = "T",
                NormalizedTitleKey = "t",
                MetadataJson = "{}",
                CreatedAt = DateTimeOffset.UnixEpoch,
                LibraryPath = "/library/T",
            });
            await seed.SaveChangesAsync();
        }

        using var context = SelectionTestSupport.CreateContext(DbPath);
        var summary = (await FollowQueries.SummariesAsync(context, EbooksOnly)).Single(s => s.Follow.Id == first);

        Assert.Equal((0, 2), (summary.Selected, summary.InLibrary));
        Assert.Empty(await FollowQueries.FindsAsync(context, first, FindShow.Selected, null, EbooksOnly));
        Assert.Equal(
            ["first Owned Ebook", "first Selected Ebook"],
            (await FollowQueries.FindsAsync(context, first, FindShow.InLibrary, null, EbooksOnly)).Select(r => r.Find.Title).Order());
    }

    [Fact]
    public async Task Finds_OfOneFollow()
    {
        var (_, second) = await SeedAsync();
        using var context = SelectionTestSupport.CreateContext(DbPath);

        var rows = await FollowQueries.FindsAsync(context, second, FindShow.All, null, EbooksOnly);

        Assert.Equal("second New Ebook", Assert.Single(rows).Find.Title);
    }
}
