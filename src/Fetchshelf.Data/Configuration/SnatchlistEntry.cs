using Fetchshelf.Core.AutoDownloader;

namespace Fetchshelf.Data.Configuration;

/// <summary>Snatch-list kind for a `[[snatchlist]]` entry.</summary>
public enum SnatchlistType
{
    Unsat,
    InactUnsat,
    SeedUnsat,
    SeedSat,
    InactSat,
    UploadsActive,
}

/// <summary>
/// One `[[snatchlist]]` entry. `cost` is restricted to `metadata_only`/
/// `metadata_only_add`; `languages` and the upload-date fields are not allowed (both enforced by
/// `ConfigLoader`).
/// </summary>
public sealed class SnatchlistEntry : SharedFilterConfig
{
    public SnatchlistType Type { get; set; }

    public ConfiguredCost Cost { get; set; } = ConfiguredCost.MetadataOnly;

    public int? MaxPages { get; set; }

    public int? SearchInterval { get; set; }

    public bool DryRun { get; set; }
}
