namespace Fetchshelf.Clients.Mam;

/// <summary>Fields a free-text search term can match against, Fetchshelf-spec.md §5.1.</summary>
public enum MamSearchField
{
    Author,
    Description,
    Filenames,
    FileTypes,
    Narrator,
    Series,
    Tags,
    Title,
}

internal static class MamSearchFieldExtensions
{
    public static string ToWireValue(this MamSearchField field) => field switch
    {
        MamSearchField.Author => "author",
        MamSearchField.Description => "description",
        MamSearchField.Filenames => "filenames",
        MamSearchField.FileTypes => "fileTypes",
        MamSearchField.Narrator => "narrator",
        MamSearchField.Series => "series",
        MamSearchField.Tags => "tags",
        MamSearchField.Title => "title",
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };
}
