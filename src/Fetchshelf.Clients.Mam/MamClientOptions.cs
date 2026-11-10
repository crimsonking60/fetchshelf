namespace Fetchshelf.Clients.Mam;

public sealed record MamClientOptions
{
    public string BaseUrl { get; init; } = "https://www.myanonamouse.net";

    /// <summary>Snatch lists are served from a separate host.</summary>
    public string CdnBaseUrl { get; init; } = "https://cdn.myanonamouse.net";

    /// <summary>The configured `mam_id` (session cookie value), sent with every MAM request.</summary>
    public required string MamId { get; init; }

    /// <summary>
    /// Sent with every MAM request. The host sets it to <c>Fetchshelf/&lt;version&gt;</c>, so MAM's staff can tell
    /// releases apart; the default carries no version rather than a made-up one.
    /// </summary>
    public string UserAgent { get; init; } = "Fetchshelf";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
