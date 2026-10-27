using System.Text.Json.Serialization;

namespace Fetchshelf.Clients.Notion;

/// <summary>
/// A Notion page property, discriminated by its <c>type</c> field, Fetchshelf-spec.md §5.4. Only
/// <see cref="NotionUrlProperty"/> carries data this client acts on (MaM ids are read from URL
/// properties); the others are modeled so the row's full property set deserializes without error.
/// Deserialized by <see cref="NotionPropertyConverter"/> rather than attribute-based polymorphism:
/// the real Notion API puts <c>id</c> before <c>type</c> in the wire JSON, and .NET's
/// <c>JsonPolymorphic</c>/<c>JsonDerivedType</c> attributes only recognize the discriminator as the
/// object's first property.
/// </summary>
[JsonConverter(typeof(NotionPropertyConverter))]
public abstract record NotionProperty
{
    public string? Id { get; init; }
}

public sealed record NotionDateProperty : NotionProperty;

public sealed record NotionFilesProperty : NotionProperty;

public sealed record NotionNumberProperty : NotionProperty;

public sealed record NotionRelationProperty : NotionProperty;

public sealed record NotionSelectProperty : NotionProperty;

public sealed record NotionMultiSelectProperty : NotionProperty;

public sealed record NotionStatusProperty : NotionProperty;

public sealed record NotionRichTextProperty : NotionProperty;

public sealed record NotionTitleProperty : NotionProperty;

/// <summary>A URL-type property; <see cref="Url"/> is null when the field is unset on a given row.</summary>
public sealed record NotionUrlProperty : NotionProperty
{
    public string? Url { get; init; }
}

/// <summary>A property type not among the known variants (forward compatibility with new Notion property types).</summary>
public sealed record NotionUnknownProperty : NotionProperty;
