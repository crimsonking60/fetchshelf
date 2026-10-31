using System.Text.Json.Serialization;

namespace Fetchshelf.Clients.Abs;

/// <summary>`GET /api/libraries/{id}/search?q=` response.</summary>
public sealed record AbsSearchResponse
{
    [JsonPropertyName("authors")]
    public required IReadOnlyList<AbsAuthor> Authors { get; init; }
}

/// <summary>One author returned by a library author search.</summary>
public sealed record AbsAuthor
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>`GET /api/authors/{id}?include=items` response.</summary>
public sealed record AbsAuthorResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("libraryItems")]
    public required IReadOnlyList<AbsLibraryItem> LibraryItems { get; init; }
}

/// <summary>One of an author's library items.</summary>
public sealed record AbsLibraryItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }
}
