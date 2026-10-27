using System.Text.Json.Serialization;

namespace Fetchshelf.Clients.Abs;

/// <summary>`GET /api/libraries` response, Fetchshelf-spec.md §5.3.</summary>
public sealed record AbsLibrariesResponse
{
    [JsonPropertyName("libraries")]
    public required IReadOnlyList<AbsLibrary> Libraries { get; init; }
}

/// <summary>One Audiobookshelf library, with the folders it watches.</summary>
public sealed record AbsLibrary
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("folders")]
    public required IReadOnlyList<AbsFolder> Folders { get; init; }
}

/// <summary>One folder watched by an Audiobookshelf library.</summary>
public sealed record AbsFolder
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("fullPath")]
    public required string FullPath { get; init; }
}
