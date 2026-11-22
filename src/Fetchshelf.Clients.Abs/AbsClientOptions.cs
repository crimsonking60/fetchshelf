namespace Fetchshelf.Clients.Abs;

/// <summary>Connection details for the Audiobookshelf client.</summary>
public sealed record AbsClientOptions
{
    public required string BaseUrl { get; init; }

    public required string Token { get; init; }

    /// <summary>`genres = true`: the genres sent are MAM's names, with Fiction or Nonfiction.</summary>
    public bool MamGenreNames { get; init; }
}
