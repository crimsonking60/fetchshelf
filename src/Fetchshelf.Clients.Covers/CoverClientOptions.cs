namespace Fetchshelf.Clients.Covers;

/// <summary>Settings of the cover clients. The addresses are the services' own; tests point them at fake servers.</summary>
public sealed class CoverClientOptions
{
    /// <summary>Sent with every request, naming Fetchshelf and its version only.</summary>
    public required string UserAgent { get; init; }

    public Uri OpenLibraryBaseUrl { get; init; } = new("https://covers.openlibrary.org/");

    public Uri AudnexusBaseUrl { get; init; } = new("https://api.audnex.us/");

    /// <summary>
    /// Open Library allows about 100 cover lookups by ISBN per 5 minutes from one address, so one every 3 seconds.
    /// </summary>
    public TimeSpan OpenLibrarySpacing { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan AudnexusSpacing { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The largest cover read; a larger one is no cover.</summary>
    public int MaxImageBytes { get; init; } = 5 * 1024 * 1024;
}
