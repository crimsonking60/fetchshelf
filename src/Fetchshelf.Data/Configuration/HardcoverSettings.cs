namespace Fetchshelf.Data.Configuration;

/// <summary>
/// The optional `[hardcover]` section: the personal access token for Hardcover's API. Absent entirely (null on
/// <see cref="FetchshelfSettings.Hardcover"/>) means no `[[hardcover_list]]` can run.
/// </summary>
public sealed class HardcoverSettings
{
    /// <summary>When <see cref="TokenFile"/> is set, <c>ConfigLoader</c> fills this from that file.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Full path of a file holding the token; an alternative to setting <see cref="Token"/>.</summary>
    public string? TokenFile { get; set; }
}
