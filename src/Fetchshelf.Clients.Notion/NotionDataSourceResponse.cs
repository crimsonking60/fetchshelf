namespace Fetchshelf.Clients.Notion;

/// <summary>A Notion data source query response.</summary>
public sealed record NotionDataSourceResponse
{
    public IReadOnlyList<NotionRow> Results { get; init; } = [];

    public string? NextCursor { get; init; }

    public bool HasMore { get; init; }
}

/// <summary>One row (page) from a Notion data source query.</summary>
public sealed record NotionRow
{
    public required string Id { get; init; }

    public IReadOnlyDictionary<string, NotionProperty> Properties { get; init; } =
        new Dictionary<string, NotionProperty>();
}
