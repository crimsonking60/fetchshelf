namespace Fetchshelf.Clients.Mam;

public sealed record MamClientOptions
{
    public string BaseUrl { get; init; } = "https://www.myanonamouse.net";

    /// <summary>Snatch lists are served from a separate host.</summary>
    public string CdnBaseUrl { get; init; } = "https://cdn.myanonamouse.net";

    /// <summary>The configured `mam_id` (session cookie value), sent with every MAM request.</summary>
    public required string MamId { get; init; }

    public string UserAgent { get; init; } = "Fetchshelf/0.1.0";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
