using System.Globalization;
using System.Text.Json;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Statistics;

/// <summary>A label and how many there are of it, such as a media type and its books.</summary>
public sealed record CountRow(string Label, int Count);

/// <summary>One month's numbers, by the month's <c>yyyy-MM</c> in UTC.</summary>
/// <param name="Month">The month, such as <c>2026-11</c>.</param>
/// <param name="Values">The numbers by column, such as grabs by cost; a column with nothing that month is 0.</param>
public sealed record MonthRow(string Month, IReadOnlyDictionary<string, long> Values)
{
    public long Total => Values.Values.Sum();
}

/// <summary>A job's runs over a period: how many, how many failed, and how many ended with some items failed.</summary>
public sealed record JobRunCounts(string Job, int Runs, int Failed, int WithErrors);

/// <summary>
/// The Statistics page's numbers, each table one query. SQLite can't compare or group `DateTimeOffset` values, so a
/// query narrows what it can in the database, selects the few columns it needs, and groups by month in memory: fine for
/// tens of thousands of torrents and events.
/// </summary>
public sealed class LibraryStatistics(FetchshelfDbContext dbContext)
{
    /// <summary>The cost columns of the grab tables, in the order they are shown.</summary>
    public static readonly IReadOnlyList<string> CostColumns = [Free, Vip, PersonalFreeleech, Wedge, Ratio];

    public const string Free = "Freeleech";
    public const string Vip = "VIP";
    public const string PersonalFreeleech = "Personal FL";
    public const string Wedge = "Wedge";
    public const string Ratio = "Ratio";

    /// <summary>The columns of the bytes-downloaded table: what cost ratio, and what didn't.</summary>
    public static readonly IReadOnlyList<string> RatioColumns = [Ratio, "Free"];

    /// <summary>The books in the library: linked torrents that weren't replaced or unlinked since.</summary>
    private IQueryable<TorrentEntity> Library => dbContext.Torrents.AsNoTracking().Where(t => t.LibraryPath != null);

    public async Task<IReadOnlyList<CountRow>> BooksByMediaTypeAsync(CancellationToken cancellationToken = default) =>
        Ordered(await Library.GroupBy(t => t.MediaType).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken),
            g => g.Key, g => g.Count);

    public async Task<IReadOnlyList<CountRow>> BooksByLanguageAsync(CancellationToken cancellationToken = default) =>
        Ordered(await Library.GroupBy(t => t.Language).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken),
            g => g.Key, g => g.Count);

    public async Task<IReadOnlyList<CountRow>> BooksByLibraryAsync(CancellationToken cancellationToken = default) =>
        Ordered(await Library.GroupBy(t => t.LinkerName).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken),
            g => g.Key, g => g.Count);

    /// <summary>
    /// Books by the format the Link job chose for them, such as m4b or epub. A book linked with both an audio and an ebook
    /// format counts under each.
    /// </summary>
    public async Task<IReadOnlyList<CountRow>> BooksByFormatAsync(CancellationToken cancellationToken = default)
    {
        var audio = await Library.Where(t => t.ChosenAudioExtension != null)
            .GroupBy(t => t.ChosenAudioExtension).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var ebook = await Library.Where(t => t.ChosenEbookExtension != null)
            .GroupBy(t => t.ChosenEbookExtension).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return Ordered(
            audio.Concat(ebook).GroupBy(g => g.Key!.ToLowerInvariant()).Select(g => new { Key = (string?)g.Key, Count = g.Sum(x => x.Count) }),
            g => g.Key, g => g.Count);
    }

    /// <summary>The size of the files linked into the libraries.</summary>
    public async Task<long> LibraryBytesAsync(CancellationToken cancellationToken = default)
    {
        var files = await Library.Where(t => t.LinkedFilesJson != null).Select(t => t.LinkedFilesJson!).ToListAsync(cancellationToken);
        return files.Sum(json => (JsonSerializer.Deserialize<List<LinkedFileRecord>>(json) ?? []).Sum(f => f.SizeBytes));
    }

    /// <summary>Books added to the library per month, by when Fetchshelf first knew them.</summary>
    public async Task<IReadOnlyList<MonthRow>> BooksAddedPerMonthAsync(DateTimeOffset now, int months = 12, CancellationToken cancellationToken = default)
    {
        var created = await Library.Select(t => t.CreatedAt).ToListAsync(cancellationToken);
        return PerMonth(now, months, created.Select(c => (c, "Books", 1L)), ["Books"]);
    }

    /// <summary>Torrents grabbed per month, by what they cost (<see cref="CostColumns"/>).</summary>
    public async Task<IReadOnlyList<MonthRow>> GrabsPerMonthAsync(DateTimeOffset now, int months = 12, CancellationToken cancellationToken = default)
    {
        var grabs = await GrabsAsync(cancellationToken);
        return PerMonth(now, months, grabs.Select(g => (g.OccurredAt, CostColumn(g.Cost, g.WedgeUsed), 1L)), CostColumns);
    }

    /// <summary>The size of the torrents grabbed per month: those that cost ratio, and the rest.</summary>
    public async Task<IReadOnlyList<MonthRow>> BytesGrabbedPerMonthAsync(DateTimeOffset now, int months = 12, CancellationToken cancellationToken = default)
    {
        var since = MonthStart(now, months);
        var grabs = (await GrabsAsync(cancellationToken)).Where(g => g.OccurredAt >= since).ToList();
        var ids = grabs.Select(g => g.TorrentId).OfType<string>().Distinct().ToList();
        var sizes = (await dbContext.Torrents.AsNoTracking()
                .Where(t => ids.Contains(t.Id))
                .Select(t => new { t.Id, t.MetadataJson })
                .ToListAsync(cancellationToken))
            .ToDictionary(t => t.Id, t => TorrentMetadataSerializer.Deserialize(t.MetadataJson).TotalSizeBytes);
        return PerMonth(now, months,
            grabs.Select(g => (g.OccurredAt, CostColumn(g.Cost, g.WedgeUsed) == Ratio ? Ratio : "Free", g.TorrentId is { } id ? sizes.GetValueOrDefault(id) : 0)),
            RatioColumns);
    }

    /// <summary>Torrents grabbed in the last <paramref name="months"/> months by each autograb, list, follow or other grabber, most first.</summary>
    public async Task<IReadOnlyList<CountRow>> GrabsByGrabberAsync(DateTimeOffset now, int months = 12, CancellationToken cancellationToken = default)
    {
        var since = MonthStart(now, months);
        return (await GrabsAsync(cancellationToken))
            .Where(g => g.OccurredAt >= since)
            .GroupBy(g => g.GrabberName ?? "")
            .Select(g => new CountRow(g.Key, g.Count()))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Each job's runs that started in the last <paramref name="days"/> days, by job name.</summary>
    public async Task<IReadOnlyList<JobRunCounts>> JobRunsAsync(DateTimeOffset now, int days = 30, CancellationToken cancellationToken = default)
    {
        var since = now.AddDays(-days);
        var runs = await dbContext.JobRuns.AsNoTracking()
            .Select(r => new { r.JobName, r.StartedAt, r.Result })
            .ToListAsync(cancellationToken);
        return runs
            .Where(r => r.StartedAt >= since)
            .GroupBy(r => r.JobName)
            .Select(g => new JobRunCounts(
                g.Key, g.Count(), g.Count(r => r.Result == JobRunResult.Failure), g.Count(r => r.Result == JobRunResult.CompletedWithErrors)))
            .OrderBy(j => j.Job, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The cost column of a grab: a wedge used makes it a wedge grab whatever the cost; a `try_wedge` that found the
    /// torrent free already, or got no wedge, is counted by what it cost: ratio.
    /// </summary>
    public static string CostColumn(string? cost, bool? wedgeUsed) =>
        wedgeUsed == true ? Wedge
        : Enum.TryParse<SelectedTorrentCost>(cost, out var parsed) ? parsed switch
        {
            SelectedTorrentCost.GlobalFreeleech => Free,
            SelectedTorrentCost.Vip => Vip,
            SelectedTorrentCost.PersonalFreeleech => PersonalFreeleech,
            SelectedTorrentCost.UseWedge => Wedge,
            _ => Ratio,
        }
        : Ratio;

    /// <summary>The months shown, oldest first: the current month and the <paramref name="months"/> - 1 before it, in UTC.</summary>
    public static IReadOnlyList<string> Months(DateTimeOffset now, int months)
    {
        var first = MonthStart(now, months);
        return Enumerable.Range(0, months).Select(i => MonthKey(first.AddMonths(i))).ToList();
    }

    private sealed record Grab(DateTimeOffset OccurredAt, string? Cost, bool? WedgeUsed, string? GrabberName, string? TorrentId);

    private async Task<List<Grab>> GrabsAsync(CancellationToken cancellationToken) =>
        await dbContext.Events.AsNoTracking()
            .Where(e => e.Kind == EventKind.Grabbed)
            .Select(e => new Grab(e.OccurredAt, e.Cost, e.WedgeUsed, e.GrabberName, e.TorrentId))
            .ToListAsync(cancellationToken);

    /// <summary>The first moment of the oldest month shown.</summary>
    private static DateTimeOffset MonthStart(DateTimeOffset now, int months)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-(months - 1));
    }

    private static string MonthKey(DateTimeOffset time) => time.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Sums each item's value into its month and column; every month and column shown is there, 0 when empty.</summary>
    private static List<MonthRow> PerMonth(
        DateTimeOffset now, int months, IEnumerable<(DateTimeOffset At, string Column, long Value)> items, IReadOnlyList<string> columns)
    {
        var table = Months(now, months).ToDictionary(m => m, _ => columns.ToDictionary(c => c, _ => 0L));
        foreach (var (at, column, value) in items)
        {
            if (table.TryGetValue(MonthKey(at), out var row) && row.ContainsKey(column))
            {
                row[column] += value;
            }
        }

        return table.Select(r => new MonthRow(r.Key, r.Value)).ToList();
    }

    /// <summary>Rows most first, a missing label shown as "Unknown".</summary>
    private static List<CountRow> Ordered<T>(IEnumerable<T> groups, Func<T, string?> label, Func<T, int> count) =>
        groups.Select(g => new CountRow(string.IsNullOrEmpty(label(g)) ? "Unknown" : label(g)!, count(g)))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
