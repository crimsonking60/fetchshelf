namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// Builds a Torrent record's synthetic id for a metadata-only entry, a string derived from the MAM id.
/// Used by selection (`metadata_only_add`); records the removed snatchlist import made use the same convention.
/// </summary>
public static class MetadataOnlyTorrentId
{
    private const string Prefix = "mam:";

    public static string For(long mamId) => $"{Prefix}{mamId}";

    public static bool IsMetadataOnly(string id) => id.StartsWith(Prefix, StringComparison.Ordinal);
}
