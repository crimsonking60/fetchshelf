namespace Fetchshelf.Web.Jobs;

/// <summary>The bulk operations a page shows (<c>_BulkOperationsPartial</c>), and the page, whose handlers the panel calls.</summary>
/// <param name="Page">The Razor Page, such as "/Torrents/Index", with the BulkOperations, CancelBulk and DismissBulk handlers.</param>
public sealed record BulkOperationsViewModel(IReadOnlyList<BulkOperation> Operations, string Page);
