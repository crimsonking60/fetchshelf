using System.Text.Json.Serialization;

namespace Fetchshelf.Clients.Abs;

/// <summary>`PATCH /api/items/{id}/media` request body.</summary>
public sealed record AbsMetadataUpdateRequest
{
    [JsonPropertyName("metadata")]
    public required AbsMetadata Metadata { get; init; }
}

public sealed record AbsMetadata
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("authors")]
    public required IReadOnlyList<AbsAuthorName> Authors { get; init; }

    [JsonPropertyName("narrators")]
    public required IReadOnlyList<string> Narrators { get; init; }

    [JsonPropertyName("series")]
    public required IReadOnlyList<AbsSeries> Series { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("isbn")]
    public string? Isbn { get; init; }

    [JsonPropertyName("asin")]
    public string? Asin { get; init; }

    [JsonPropertyName("genres")]
    public required IReadOnlyList<string> Genres { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("explicit")]
    public required bool Explicit { get; init; }

    [JsonPropertyName("abridged")]
    public required bool Abridged { get; init; }
}

public sealed record AbsAuthorName([property: JsonPropertyName("name")] string Name);

public sealed record AbsSeries
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("sequence")]
    public string? Sequence { get; init; }
}
