namespace Fetchshelf.Data.Configuration;

/// <summary>The optional `[audiobookshelf]` section, Fetchshelf-spec.md §4.1, §4.2, §9. Absent entirely (null
/// on <see cref="FetchshelfSettings.Audiobookshelf"/>) means the integration is off.</summary>
public sealed class AudiobookshelfSettings
{
    public string Url { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public int Interval { get; set; } = 10;
}
