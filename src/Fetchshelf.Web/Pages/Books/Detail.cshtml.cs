using Fetchshelf.Core.Catalog;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Web.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Books;

/// <summary>One book of the Books page: its own page, and the body of the dialog the Books page opens.</summary>
public class DetailModel(BookCatalogCache catalogCache, FetchshelfSettings settings) : PageModel
{
    public CatalogBook? Book { get; private set; }

    /// <summary>The book's library, as the page names it.</summary>
    public string? LibraryName { get; private set; }

    /// <summary>"Open in Audiobookshelf", for a matched book when `[audiobookshelf]` is configured.</summary>
    public string? AbsLink { get; private set; }

    /// <summary>Whether its files can be downloaded: only with an `[opds]` section.</summary>
    public bool DownloadsAvailable => BookEndpoints.DownloadsAvailable(settings);

    /// <summary>Whether the downloads ask for the `[opds]` login.</summary>
    public bool DownloadsNeedLogin => settings.Opds?.RequiresLogin == true;

    public bool InDialog { get; private set; }

    public string LanguageName => Book is null ? string.Empty : LanguageNameResolver.DisplayName(Book.Language);

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken) =>
        await LoadAsync(id, cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnGetPartialAsync(string id, CancellationToken cancellationToken)
    {
        InDialog = true;
        return await LoadAsync(id, cancellationToken) ? Partial("_BookDetailPartial", this) : NotFound();
    }

    private async Task<bool> LoadAsync(string id, CancellationToken cancellationToken)
    {
        var catalog = await catalogCache.GetAsync(cancellationToken);
        if (catalog.Find(id) is not { } book)
        {
            return false;
        }

        Book = book;
        LibraryName = catalog.Libraries.FirstOrDefault(library => library.Index == book.LibraryIndex)?.Name;
        AbsLink = book.AbsId is not null && settings.Audiobookshelf?.Url is { } absUrl
            ? $"{absUrl.TrimEnd('/')}/audiobookshelf/item/{book.AbsId}"
            : null;
        return true;
    }

    /// <summary>A file's size as people read it: "812 KB", "1.4 GB".</summary>
    public static string Size(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} bytes"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{size:0.#} {units[unit]}");
    }
}
