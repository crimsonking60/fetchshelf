using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Data.Entities;

/// <summary>
/// One author or narrator of a torrent: a row derived from <see cref="TorrentEntity.MetadataJson"/> when it is saved
/// (see <see cref="FetchshelfDbContext"/>), never written on its own. Deleted with its torrent.
/// </summary>
public sealed class TorrentPersonEntity
{
    public long Id { get; set; }

    public required string TorrentId { get; set; }

    public required PersonRole Role { get; set; }

    /// <summary>The name's place among the torrent's authors, or among its narrators.</summary>
    public required int Position { get; set; }

    /// <summary>As written in the metadata.</summary>
    public required string Name { get; set; }

    /// <summary><see cref="PersonNameKey"/> of the name: what queries match on.</summary>
    public required string NameKey { get; set; }
}

/// <summary>
/// One entry of one of a torrent's series, derived like <see cref="TorrentPersonEntity"/>. A series without a number
/// has one row with no kind and no numbers.
/// </summary>
public sealed class TorrentSeriesEntity
{
    public long Id { get; set; }

    public required string TorrentId { get; set; }

    /// <summary>The row's place among the torrent's series entries.</summary>
    public required int Position { get; set; }

    public required string Name { get; set; }

    /// <summary><see cref="SeriesNameKey"/> of the name: what queries match on.</summary>
    public required string NameKey { get; set; }

    public SeriesEntryKind? Kind { get; set; }

    /// <summary>A double rather than the metadata's decimal: SQLite stores decimals as text, which can't be compared in SQL.</summary>
    public double? FirstNumber { get; set; }

    /// <summary>The last book number the entry covers: the end of a range, else the same as <see cref="FirstNumber"/>.</summary>
    public double? LastNumber { get; set; }

    /// <summary>The entry as shown: "3", "1-3", "2p1".</summary>
    public string? Display { get; set; }
}
