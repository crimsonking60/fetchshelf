namespace Fetchshelf.Data.Configuration;

/// <summary>The `categories` field's four main-type entries.</summary>
public sealed class CategoriesConfig
{
    public CategoryListOrBool Audio { get; set; } = CategoryListOrBool.All;

    public CategoryListOrBool Ebook { get; set; } = CategoryListOrBool.All;

    public CategoryListOrBool Musicology { get; set; } = CategoryListOrBool.None;

    public CategoryListOrBool Radio { get; set; } = CategoryListOrBool.None;
}

/// <summary>
/// The shared torrent filter's config-shaped fields, embedded into
/// `[[autograb]]`/`[[tag]]`/`[[snatchlist]]` blocks.
/// </summary>
public class SharedFilterConfig
{
    public string? Name { get; set; }

    public List<string> MediaType { get; set; } = [];

    public CategoriesConfig Categories { get; set; } = new();

    public List<string> Languages { get; set; } = [];

    public Dictionary<string, bool> Flags { get; set; } = [];

    public string? MinSize { get; set; }

    public string? MaxSize { get; set; }

    public List<string> ExcludeUploader { get; set; } = [];

    public string? UploadedAfter { get; set; }

    public string? UploadedBefore { get; set; }

    public long? MinSeeders { get; set; }

    public long? MaxSeeders { get; set; }

    public long? MinLeechers { get; set; }

    public long? MaxLeechers { get; set; }

    public long? MinSnatched { get; set; }

    public long? MaxSnatched { get; set; }

    public int? MaxAuthors { get; set; }
}
