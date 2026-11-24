namespace Fetchshelf.Web.Jobs;

/// <summary>The bulk operations a page shows (<c>_BulkOperationsPartial</c>), and the page, whose handlers the panel calls.</summary>
/// <param name="Page">The Razor Page, such as "/Torrents/Index", with the BulkOperations, CancelBulk and DismissBulk handlers.</param>
/// <param name="RouteId">The page's <c>{id}</c>, for a torrent's page, which the panel's URLs keep; null for the other pages.</param>
public sealed record BulkOperationsViewModel(IReadOnlyList<BulkOperation> Operations, string Page, string? RouteId = null)
{
    /// <summary>The link an ended operation offers, to see what it changed.</summary>
    public string ReloadLabel => RouteId is null ? "Reload the list" : "Reload the page";
}
