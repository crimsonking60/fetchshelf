namespace Fetchshelf.Web.Pages;

/// <summary>What the Search page shows after an in-place download: the outcome message and the result's row with its new status.</summary>
public sealed record DownloadFeedbackViewModel(string Message, SearchResultViewModel? Row, bool IsError = false);
