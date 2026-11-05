namespace Fetchshelf.Clients.Hardcover;

/// <summary>Connection details for the Hardcover client.</summary>
public sealed record HardcoverClientOptions
{
    public const string DefaultEndpoint = "https://api.hardcover.app/v1/graphql";

    /// <summary>A personal access token from Hardcover's account settings, with or without its "Bearer " prefix.</summary>
    public required string Token { get; init; }

    /// <summary>Sent as the User-Agent, which Hardcover asks scripts to set, e.g. "Fetchshelf/2026.11.2".</summary>
    public required string UserAgent { get; init; }

    public string Endpoint { get; init; } = DefaultEndpoint;
}
