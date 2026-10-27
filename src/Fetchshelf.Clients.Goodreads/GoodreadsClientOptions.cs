namespace Fetchshelf.Clients.Goodreads;

/// <summary>Connection details for the Goodreads client, Fetchshelf-spec.md §5.4: "a browser-like user agent is required."</summary>
public sealed record GoodreadsClientOptions
{
    public string UserAgent { get; init; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36";
}
