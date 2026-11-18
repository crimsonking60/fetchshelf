using System.Text.Json;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Data.Statistics;

namespace Fetchshelf.Data.Tests;

public sealed class LibraryStatisticsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-library-statistics-tests-").FullName;

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private FetchshelfDbContext CreateContext() => OrganizerTestSupport.CreateContext(Path.Combine(_root, "test.db"));

    /// <summary>A torrent in the library, or not when <paramref name="library"/> is null.</summary>
    private static async Task<TorrentEntity> AddTorrentAsync(
        FetchshelfDbContext context, long mamId, string? library, MediaType mediaType = MediaType.Audiobook, Language language = Language.English,
        long sizeBytes = 100, DateTimeOffset? createdAt = null)
    {
        var torrent = await OrganizerTestSupport.SeedTorrentAsync(context, $"hash{mamId}", mamId);
        // The MediaType and Language columns follow the metadata when it is saved.
        var metadata = TorrentMetadataSerializer.Deserialize(torrent.MetadataJson) with { TotalSizeBytes = sizeBytes, MediaType = mediaType, Language = language };
        torrent.MetadataJson = TorrentMetadataSerializer.Serialize(metadata);
        torrent.CreatedAt = createdAt ?? Now;
        if (library is not null)
        {
            torrent.LibraryPath = $"/library/{library}/{mamId}";
            torrent.LinkerName = library;
            torrent.LinkedFilesJson = JsonSerializer.Serialize(new List<LinkedFileRecord> { new("a.m4b", sizeBytes), new("b.m4b", 1) });
        }

        await context.SaveChangesAsync();
        return torrent;
    }

    private static void AddGrab(FetchshelfDbContext context, long mamId, string cost, DateTimeOffset at, bool wedgeUsed = false, string? grabber = "New books") =>
        context.Events.Add(new EventEntity
        {
            OccurredAt = at,
            TorrentId = $"hash{mamId}",
            MamId = mamId,
            Kind = EventKind.Grabbed,
            Cost = cost,
            WedgeUsed = wedgeUsed,
            GrabberName = grabber,
        });

    [Fact]
    public async Task Books_ByMediaTypeLanguageAndLibrary_OnlyThoseInTheLibrary_MostFirst()
    {
        using var context = CreateContext();
        await AddTorrentAsync(context, 1, "Audiobooks");
        await AddTorrentAsync(context, 2, "Audiobooks", language: Language.German);
        await AddTorrentAsync(context, 3, "Ebooks", mediaType: MediaType.Ebook);
        await AddTorrentAsync(context, 4, library: null);
        var statistics = new LibraryStatistics(context);

        Assert.Equal([new CountRow("Audiobook", 2), new CountRow("Ebook", 1)], await statistics.BooksByMediaTypeAsync());
        Assert.Equal([new CountRow("English", 2), new CountRow("German", 1)], await statistics.BooksByLanguageAsync());
        Assert.Equal([new CountRow("Audiobooks", 2), new CountRow("Ebooks", 1)], await statistics.BooksByLibraryAsync());
        Assert.Equal(3 * (100 + 1), await statistics.LibraryBytesAsync());
    }

    [Fact]
    public async Task BooksAddedPerMonth_TwelveMonthsOldestFirst_EmptyMonthsAreZero()
    {
        using var context = CreateContext();
        await AddTorrentAsync(context, 1, "Audiobooks", createdAt: Now);
        await AddTorrentAsync(context, 2, "Audiobooks", createdAt: Now.AddMonths(-2));
        await AddTorrentAsync(context, 3, "Audiobooks", createdAt: Now.AddMonths(-13));

        var months = await new LibraryStatistics(context).BooksAddedPerMonthAsync(Now);

        Assert.Equal(12, months.Count);
        Assert.Equal("2025-12", months[0].Month);
        Assert.Equal("2026-11", months[^1].Month);
        Assert.Equal(1, months.Single(m => m.Month == "2026-09").Total);
        Assert.Equal(1, months[^1].Total);
        Assert.Equal(2, months.Sum(m => m.Total));
    }

    [Fact]
    public async Task GrabsPerMonth_ByCost_AWedgeUsedIsAWedgeGrab()
    {
        using var context = CreateContext();
        AddGrab(context, 1, "GlobalFreeleech", Now);
        AddGrab(context, 2, "Vip", Now);
        AddGrab(context, 3, "PersonalFreeleech", Now);
        AddGrab(context, 4, "TryWedge", Now, wedgeUsed: true);
        AddGrab(context, 5, "TryWedge", Now);
        AddGrab(context, 6, "Ratio", Now.AddMonths(-1));
        context.Events.Add(new EventEntity { OccurredAt = Now, Kind = EventKind.Linked, TorrentId = "hash1" });
        await context.SaveChangesAsync();

        var months = await new LibraryStatistics(context).GrabsPerMonthAsync(Now);

        var november = months[^1].Values;
        Assert.Equal((1L, 1L, 1L, 1L, 1L), (november["Freeleech"], november["VIP"], november["Personal FL"], november["Wedge"], november["Ratio"]));
        Assert.Equal(1, months[^2].Values["Ratio"]);
        Assert.Equal(0, months[0].Total);
    }

    [Fact]
    public async Task BytesGrabbedPerMonth_RatioAgainstFree_ByTheTorrentsSize()
    {
        using var context = CreateContext();
        await AddTorrentAsync(context, 1, library: null, sizeBytes: 1000);
        await AddTorrentAsync(context, 2, library: null, sizeBytes: 300);
        await AddTorrentAsync(context, 3, library: null, sizeBytes: 50);
        AddGrab(context, 1, "Ratio", Now);
        AddGrab(context, 2, "Vip", Now);
        AddGrab(context, 3, "UseWedge", Now, wedgeUsed: true);
        await context.SaveChangesAsync();

        var november = (await new LibraryStatistics(context).BytesGrabbedPerMonthAsync(Now))[^1].Values;

        Assert.Equal((1000L, 350L), (november["Ratio"], november["Free"]));
    }

    [Fact]
    public async Task GrabsByGrabber_LastTwelveMonths_MostFirst()
    {
        using var context = CreateContext();
        AddGrab(context, 1, "Ratio", Now, grabber: "Follow: Discworld");
        AddGrab(context, 2, "Ratio", Now, grabber: "New books");
        AddGrab(context, 3, "Ratio", Now, grabber: "New books");
        AddGrab(context, 4, "Ratio", Now.AddMonths(-14), grabber: "Old list");
        await context.SaveChangesAsync();

        Assert.Equal(
            [new CountRow("New books", 2), new CountRow("Follow: Discworld", 1)],
            await new LibraryStatistics(context).GrabsByGrabberAsync(Now));
    }

    [Fact]
    public async Task JobRuns_LastThirtyDays_CountsFailuresAndRunsWithErrors()
    {
        using var context = CreateContext();
        foreach (var (job, result, daysAgo) in new[]
        {
            ("Link", JobRunResult.Success, 1), ("Link", JobRunResult.Failure, 2), ("Link", JobRunResult.CompletedWithErrors, 3),
            ("Link", JobRunResult.Failure, 40), ("Download", JobRunResult.Success, 0),
        })
        {
            context.JobRuns.Add(new JobRunEntity { JobName = job, StartedAt = Now.AddDays(-daysAgo), Result = result, Trigger = JobTrigger.Manual });
        }

        await context.SaveChangesAsync();

        Assert.Equal(
            [new JobRunCounts("Download", 1, 0, 0), new JobRunCounts("Link", 3, 1, 1)],
            await new LibraryStatistics(context).JobRunsAsync(Now));
    }

    [Fact]
    public async Task EmptyDatabase_GivesEmptyTablesAndZeroMonths()
    {
        using var context = CreateContext();
        var statistics = new LibraryStatistics(context);

        Assert.Empty(await statistics.BooksByMediaTypeAsync());
        Assert.Equal(0, await statistics.LibraryBytesAsync());
        Assert.All(await statistics.GrabsPerMonthAsync(Now), m => Assert.Equal(0, m.Total));
        Assert.Empty(await statistics.GrabsByGrabberAsync(Now));
        Assert.Empty(await statistics.JobRunsAsync(Now));
    }
}
