namespace Fetchshelf.Clients.Mam;

/// <summary>Connection details for the MAM client.</summary>
public sealed record MamClientOptions
{
    public string BaseUrl { get; init; } = "https://www.myanonamouse.net";

    /// <summary>Snatch lists are served from a separate host (confirmed against
    /// the original Rust implementation's `mlm_mam::api::MAM::snatchlist`).</summary>
    public string CdnBaseUrl { get; init; } = "https://cdn.myanonamouse.net";

    /// <summary>The configured `mam_id` (session cookie value), used when no stored cookie exists
    /// or the stored cookie is rejected.</summary>
    public required string MamId { get; init; }

    public string UserAgent { get; init; } = "Fetchshelf/0.1.0";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
