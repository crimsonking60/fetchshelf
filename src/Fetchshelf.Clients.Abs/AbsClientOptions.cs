namespace Fetchshelf.Clients.Abs;

/// <summary>Connection details for the Audiobookshelf client, Fetchshelf-spec.md §4.2, §5.3.</summary>
public sealed record AbsClientOptions
{
    public required string BaseUrl { get; init; }

    public required string Token { get; init; }
}
