using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>Bound form fields for the manual metadata edit page.</summary>
public sealed class EditInputModel
{
    public string Title { get; set; } = string.Empty;

    public string Authors { get; set; } = string.Empty;

    public string Narrators { get; set; } = string.Empty;

    public string Series { get; set; } = string.Empty;

    public Language Language { get; set; } = Language.English;

    public LegacyCategory Category { get; set; } = LegacyCategory.AudiobookGeneralFiction;

    public List<string> Flags { get; set; } = [];

    public static EditInputModel From(TorrentMetadata metadata) => new()
    {
        Title = metadata.Title,
        Authors = string.Join('\n', metadata.Authors),
        Narrators = string.Join('\n', metadata.Narrators),
        Series = string.Join('\n', metadata.Series.Select(SeriesParser.FormatLine)),
        Language = metadata.Language,
        Category = metadata.LegacyCategory,
        Flags = Enum.GetValues<BookFlags>().Where(f => f != BookFlags.None && metadata.Flags.HasFlag(f)).Select(f => f.ToString()).ToList(),
    };

    /// <summary>
    /// The form's values applied onto <paramref name="original"/>, the metadata the form was opened with, so the fields
    /// it doesn't show (MAM id, description, ISBN, file types, size, edition and more) survive the edit. Without
    /// original metadata, the rest is left empty.
    /// </summary>
    public TorrentMetadata ToMetadata(TorrentMetadata? original)
    {
        var flags = Flags.Aggregate(BookFlags.None, (acc, f) => acc | Enum.Parse<BookFlags>(f));
        var baseline = original ?? new TorrentMetadata
        {
            MamId = 0,
            VipStatus = VipStatus.NotVip,
            MediaType = LegacyCategoryGroups.MediaTypeOf(Category),
            LegacyCategory = Category,
            Language = Language,
            Title = Title,
            MetadataSource = MetadataSource.Manual,
            UploadTime = DateTimeOffset.UtcNow,
        };

        return baseline with
        {
            MediaType = LegacyCategoryGroups.MediaTypeOf(Category),
            LegacyCategory = Category,
            Language = Language,
            Flags = flags,
            Title = Title,
            Authors = SplitLines(Authors),
            Narrators = SplitLines(Narrators),
            Series = SplitLines(Series).Select(SeriesParser.ParseLine).ToList(),
            MetadataSource = MetadataSource.Manual,
        };
    }

    private static List<string> SplitLines(string? text) => (text ?? string.Empty)
        .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .ToList();
}
