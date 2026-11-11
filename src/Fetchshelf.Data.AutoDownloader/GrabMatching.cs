using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What a `grab` block (of a Hardcover list, `[follow]` or `[upgrade]`) checks of a MAM search result.</summary>
internal static class GrabMatching
{
    /// <summary>Whether a grab block with <paramref name="cost"/> may take <paramref name="record"/>: the rule selection applies.</summary>
    public static bool Accepts(ConfiguredCost cost, MamTorrentRecord record) =>
        CostPolicy.Derive(record.IsVip, record.IsPersonalFreeleech, record.IsFree, cost) is not null;

    /// <summary>The result as the shared filter sees it.</summary>
    public static TorrentFilterCandidate ToFilterCandidate(MamTorrentRecord record, TorrentMetadata metadata) => new()
    {
        Metadata = metadata,
        UploaderName = record.UploaderName,
        Seeders = record.Seeders,
        Leechers = record.Leechers,
        TimesCompleted = record.TimesCompleted,
    };
}
