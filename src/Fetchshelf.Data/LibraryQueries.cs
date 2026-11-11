using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>An author or narrator with the spelling used most, and how many books in the library have them.</summary>
public sealed record PersonSummary(string NameKey, string Name, int Books);

/// <summary>One entry of a series the library has: "3", "1-3", or no number at all.</summary>
public sealed record OwnedSeriesEntry(string? Display, double? FirstNumber, double? LastNumber, string MediaType);

/// <summary>A series in the library, with the spelling used most, the entries owned and their media types.</summary>
public sealed record SeriesSummary(string NameKey, string Name, IReadOnlyList<OwnedSeriesEntry> Entries, int Books)
{
    public IReadOnlyList<string> MediaTypes => Entries.Select(e => e.MediaType).Distinct().Order(StringComparer.Ordinal).ToList();
}

/// <summary>
/// Questions about the library's authors, narrators and series, answered from the people and series rows rather than
/// by reading every torrent's metadata. A book counts as in the library when Fetchshelf has a torrent for it that no
/// better copy has replaced: linked, still downloading, unlinked by hand, or metadata-only, as selection counts it.
/// </summary>
public sealed class LibraryQueries(FetchshelfDbContext dbContext)
{
    /// <summary>Every author (or narrator) with their book count, by name.</summary>
    public async Task<IReadOnlyList<PersonSummary>> PeopleAsync(PersonRole role, CancellationToken cancellationToken = default)
    {
        var spellings = await (
                from person in dbContext.TorrentPeople
                join torrent in dbContext.Torrents on person.TorrentId equals torrent.Id
                where person.Role == role && torrent.ReplacedWithTorrentId == null
                group person by new { person.NameKey, person.Name } into g
                select new { g.Key.NameKey, g.Key.Name, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return spellings
            .GroupBy(s => s.NameKey)
            .Select(g => new PersonSummary(g.Key, MostUsed(g.Select(s => (s.Name, s.Count))), g.Sum(s => s.Count)))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Every series with its owned entries, by name.</summary>
    public async Task<IReadOnlyList<SeriesSummary>> SeriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await OwnedSeriesRows().ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.NameKey)
            .Select(ToSummary)
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>One series with its owned entries, or null when the library has nothing in it.</summary>
    public async Task<SeriesSummary?> SeriesAsync(string nameKey, CancellationToken cancellationToken = default)
    {
        var rows = await OwnedSeriesRows(nameKey).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : ToSummary(rows.GroupBy(r => r.NameKey).Single());
    }

    /// <summary>The numbered entries of one series the library has as audiobooks, or as ebooks.</summary>
    public async Task<IReadOnlyList<OwnedSeriesEntry>> OwnedEntriesAsync(
        string seriesNameKey, FilterMainType side, CancellationToken cancellationToken = default)
    {
        var mediaTypes = MediaTypesOf(side);
        var rows = await OwnedSeriesRows(seriesNameKey, mediaTypes).ToListAsync(cancellationToken);
        // Like SeriesAsync, a book without a number in the series is no entry.
        return rows
            .Where(r => r.Display is not null)
            .Select(r => new OwnedSeriesEntry(r.Display, r.FirstNumber, r.LastNumber, r.MediaType!))
            .Distinct()
            .OrderBy(e => e.FirstNumber ?? double.MaxValue)
            .ToList();
    }

    /// <summary>The ids of the torrents with this author or narrator, replaced copies included.</summary>
    public IQueryable<string> TorrentIdsWithPerson(PersonRole role, string nameKey) =>
        dbContext.TorrentPeople.Where(p => p.Role == role && p.NameKey == nameKey).Select(p => p.TorrentId);

    /// <summary>The ids of the torrents in this series, replaced copies included.</summary>
    public IQueryable<string> TorrentIdsInSeries(string nameKey) =>
        dbContext.TorrentSeries.Where(s => s.NameKey == nameKey).Select(s => s.TorrentId);

    /// <summary>
    /// The spelling of an author's or narrator's name used most, counted like <see cref="PeopleAsync"/> among copies not
    /// replaced; among the replaced ones when only those have it; null when no torrent has it.
    /// </summary>
    public async Task<string?> PersonNameAsync(PersonRole role, string nameKey, CancellationToken cancellationToken = default) =>
        PreferringCurrentCopies(await (
                from person in dbContext.TorrentPeople
                join torrent in dbContext.Torrents on person.TorrentId equals torrent.Id
                where person.Role == role && person.NameKey == nameKey
                select new SpellingRow(person.TorrentId, person.Name, torrent.ReplacedWithTorrentId == null))
            .ToListAsync(cancellationToken));

    /// <summary>The spelling of a series name used most, counted like <see cref="PersonNameAsync"/>.</summary>
    public async Task<string?> SeriesNameAsync(string nameKey, CancellationToken cancellationToken = default) =>
        PreferringCurrentCopies(await (
                from series in dbContext.TorrentSeries
                join torrent in dbContext.Torrents on series.TorrentId equals torrent.Id
                where series.NameKey == nameKey
                select new SpellingRow(series.TorrentId, series.Name, torrent.ReplacedWithTorrentId == null))
            .ToListAsync(cancellationToken));

    private sealed record SpellingRow(string TorrentId, string Name, bool Current);

    /// <summary>The spelling most used by the copies not replaced, or by the replaced ones when there are no others.</summary>
    private static string? PreferringCurrentCopies(List<SpellingRow> rows)
    {
        var counted = rows.Any(r => r.Current) ? rows.Where(r => r.Current) : rows;
        // A torrent's several entries of one series count its spelling once.
        return MostUsedOrNull(
            counted.DistinctBy(r => (r.TorrentId, r.Name)).GroupBy(r => r.Name).Select(g => (Name: g.Key, Count: g.Count())).ToList(),
            r => r);
    }

    private sealed record SeriesRow(
        string TorrentId, string NameKey, string Name, string? Display, double? FirstNumber, double? LastNumber, string? MediaType);

    /// <summary>The series rows of torrents not replaced, of one series and some media types when given.</summary>
    private IQueryable<SeriesRow> OwnedSeriesRows(string? nameKey = null, List<string>? mediaTypes = null)
    {
        var rows =
            from series in dbContext.TorrentSeries
            join torrent in dbContext.Torrents on series.TorrentId equals torrent.Id
            where torrent.ReplacedWithTorrentId == null
            select new { series, torrent.MediaType };
        if (nameKey is not null)
        {
            rows = rows.Where(r => r.series.NameKey == nameKey);
        }

        if (mediaTypes is not null)
        {
            rows = rows.Where(r => r.MediaType != null && mediaTypes.Contains(r.MediaType));
        }

        return rows.Select(r => new SeriesRow(
            r.series.TorrentId, r.series.NameKey, r.series.Name, r.series.Display, r.series.FirstNumber, r.series.LastNumber, r.MediaType));
    }

    private static SeriesSummary ToSummary(IGrouping<string, SeriesRow> rows) =>
        new(
            rows.Key,
            // A torrent's several entries of one series count its spelling once.
            MostUsed(rows.DistinctBy(r => (r.TorrentId, r.Name)).GroupBy(r => r.Name).Select(g => (g.Key, g.Count()))),
            rows.Where(r => r.Display is not null)
                .Select(r => new OwnedSeriesEntry(r.Display, r.FirstNumber, r.LastNumber, r.MediaType ?? string.Empty))
                .Distinct()
                .OrderBy(e => e.FirstNumber)
                .ThenBy(e => e.MediaType, StringComparer.Ordinal)
                .ToList(),
            rows.Select(r => r.TorrentId).Distinct().Count());

    private static string MostUsed(IEnumerable<(string Name, int Count)> spellings) =>
        spellings.OrderByDescending(s => s.Count).ThenBy(s => s.Name, StringComparer.Ordinal).First().Name;

    private static string? MostUsedOrNull<T>(IReadOnlyList<T> rows, Func<T, (string Name, int Count)> select) =>
        rows.Count == 0 ? null : MostUsed(rows.Select(select));

    private static List<string> MediaTypesOf(FilterMainType side) =>
        Enum.GetValues<MediaType>().Where(m => FilterMainTypeMapper.FromMediaType(m) == side).Select(m => m.ToString()).ToList();
}
