namespace Fetchshelf.Web.Pages.Lists;

/// <summary>One row on the Lists index page (Fetchshelf-spec.md §10.4, §7.8).</summary>
public sealed record ListSummaryViewModel(string Id, string Name, DateTimeOffset? UpdatedAt);
