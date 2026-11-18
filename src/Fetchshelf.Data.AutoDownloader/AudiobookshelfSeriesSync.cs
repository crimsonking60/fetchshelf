using Fetchshelf.Clients.Abs;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What one sync with Audiobookshelf's series did.</summary>
/// <param name="Series">Series read from Audiobookshelf.</param>
/// <param name="Added">Series followed for the first time; in a dry run, those that would have been.</param>
/// <param name="UnknownLibraries">Names in `audiobookshelf_libraries` that are no book library in Audiobookshelf.</param>
public sealed record AudiobookshelfSeriesSyncSummary(int Series, int Added, IReadOnlyList<string> UnknownLibraries);

/// <summary>
/// Reads the series of Audiobookshelf's book libraries for `[follow] audiobookshelf_series`, at the start of each Follow run.
/// Each series not followed in any mode is followed in the configured one; a series ignored, or followed already, keeps
/// its mode, and one that leaves Audiobookshelf stays followed. The books are kept as <see cref="AudiobookshelfSeriesBookEntity"/>
/// rows, which the library counts as owned, so a book Audiobookshelf has from before Fetchshelf isn't searched for or
/// downloaded again. A dry run writes nothing.
/// </summary>
public sealed class AudiobookshelfSeriesSync(
    FetchshelfDbContext dbContext,
    AbsClient absClient,
    FollowSettings settings,
    TimeProvider? timeProvider = null,
    ILogger<AudiobookshelfSeriesSync>? logger = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ILogger<AudiobookshelfSeriesSync> _logger = logger ?? NullLogger<AudiobookshelfSeriesSync>.Instance;

    public async Task<AudiobookshelfSeriesSyncSummary> SyncAsync(bool dryRun, CancellationToken cancellationToken = default)
    {
        var bookLibraries = (await absClient.ListLibrariesAsync(cancellationToken))
            .Where(l => l.MediaType is null or "book")
            .ToList();
        var wanted = settings.AudiobookshelfLibraries;
        var chosen = wanted.Count == 0
            ? bookLibraries
            : bookLibraries.Where(l => wanted.Contains(l.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        var unknown = wanted.Where(name => !bookLibraries.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var name in unknown)
        {
            _logger.LogWarning(
                "Follow: Audiobookshelf has no book library named {Library} (audiobookshelf_libraries); its libraries are {Libraries}",
                name, string.Join(", ", bookLibraries.Select(l => l.Name)));
        }

        var series = new List<AbsSeriesInfo>();
        foreach (var library in chosen)
        {
            series.AddRange(await absClient.ListSeriesAsync(library.Id, cancellationToken: cancellationToken));
        }

        var now = _time.GetUtcNow();
        var rows = await BookRowsAsync(series, now, cancellationToken);
        var followed = (await dbContext.Follows.Where(f => f.Kind == FollowKind.Series).Select(f => f.NameKey).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var newSeries = series
            .Select(s => (Name: CleanName(s.Name), Key: SeriesNameKey.Of(CleanName(s.Name))))
            .Where(s => s.Key.Length > 0 && !followed.Contains(s.Key))
            .DistinctBy(s => s.Key)
            .ToList();

        if (!dryRun)
        {
            var mode = settings.AudiobookshelfSeries == AudiobookshelfSeriesMode.Auto ? FollowMode.Download : FollowMode.Watch;
            dbContext.Follows.AddRange(newSeries.Select(s => new FollowEntity
            {
                Kind = FollowKind.Series,
                NameKey = s.Key,
                DisplayName = s.Name,
                Mode = mode,
                Source = FollowSource.Audiobookshelf,
                CreatedAt = now,
            }));
            await dbContext.AudiobookshelfSeriesBooks.ExecuteDeleteAsync(cancellationToken);
            dbContext.AudiobookshelfSeriesBooks.AddRange(rows);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Follow: {Series} series read from Audiobookshelf ({Libraries}), {Added} {Verb}",
            series.Count, string.Join(", ", chosen.Select(l => l.Name)), newSeries.Count,
            dryRun ? "would have been followed (dry run)" : "followed for the first time");
        return new AudiobookshelfSeriesSyncSummary(series.Count, newSeries.Count, unknown);
    }

    /// <summary>
    /// One row per book and format Audiobookshelf has, leaving out the books Fetchshelf linked (their torrents count
    /// already). A book with neither audio tracks nor an ebook file counts as both: taking a book for owned only skips a
    /// download, while taking it for missing would download a copy already there.
    /// </summary>
    private async Task<List<AudiobookshelfSeriesBookEntity>> BookRowsAsync(
        IReadOnlyList<AbsSeriesInfo> series, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var linked = (await dbContext.Torrents.Where(t => t.AbsId != null).Select(t => t.AbsId!).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var rows = new List<AudiobookshelfSeriesBookEntity>();
        foreach (var one in series)
        {
            var name = CleanName(one.Name);
            var key = SeriesNameKey.Of(name);
            if (key.Length == 0)
            {
                continue;
            }

            foreach (var book in one.Books.Where(b => !linked.Contains(b.ItemId)))
            {
                var (display, first, last) = Entry(name, book.Sequence);
                var mediaTypes = (book.HasAudio, book.HasEbook) switch
                {
                    (true, false) => new[] { nameof(MediaType.Audiobook) },
                    (false, true) => [nameof(MediaType.Ebook)],
                    _ => [nameof(MediaType.Audiobook), nameof(MediaType.Ebook)],
                };
                rows.AddRange(mediaTypes.Select(mediaType => new AudiobookshelfSeriesBookEntity
                {
                    ItemId = book.ItemId,
                    Name = name,
                    NameKey = key,
                    Title = book.Title,
                    Display = display,
                    FirstNumber = first,
                    LastNumber = last,
                    MediaType = mediaType,
                    SyncedAt = now,
                }));
            }
        }

        return rows;
    }

    /// <summary>
    /// A series name without the number Audiobookshelf sometimes keeps in it from a folder name: <c>"Earthseed #1, 2"</c>
    /// is the series <c>Earthseed</c>, so it is followed once, under a name MAM's series have.
    /// </summary>
    internal static string CleanName(string name)
    {
        var at = name.IndexOf(" #", StringComparison.Ordinal);
        return (at > 0 ? name[..at] : name).Trim();
    }

    /// <summary>
    /// A book's place as the library's series rows hold it: <c>"3"</c> as 3, <c>"1-3"</c> from 1 to 3. A place that isn't
    /// a number (<c>"The Krotons"</c>) is kept as written, without a number.
    /// </summary>
    internal static (string? Display, double? First, double? Last) Entry(string seriesName, string? sequence)
    {
        if (string.IsNullOrWhiteSpace(sequence))
        {
            return (null, null, null);
        }

        var parsed = SeriesParser.Parse(seriesName, sequence);
        return parsed.Entries is [var entry, ..]
            ? (entry.Display, (double)entry.FirstNumber, (double)entry.LastNumber)
            : (sequence.Trim(), null, null);
    }
}
