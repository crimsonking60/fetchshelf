namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// When a list book still missing a format is searched for again: a day after the last search, or a week once MAM has
/// had nothing for it for a week. A book MAM doesn't have would otherwise cost a search every day for as long as it is on
/// the list.
/// </summary>
public static class ListSearchSchedule
{
    /// <summary>The wait between searches while a book is new to MAM's absence, or MAM has something for it.</summary>
    public static readonly TimeSpan Daily = TimeSpan.FromDays(1);

    /// <summary>The wait once MAM has had nothing for the book for <see cref="BackOffAfter"/>.</summary>
    public static readonly TimeSpan Weekly = TimeSpan.FromDays(7);

    /// <summary>How long MAM has had nothing for a book before it is searched only weekly.</summary>
    public static readonly TimeSpan BackOffAfter = TimeSpan.FromDays(7);

    /// <summary>When the book is next due; null when it has never been searched, so it is due now.</summary>
    /// <param name="lastSearched">When MAM was last searched for the book.</param>
    /// <param name="missingSince">Since when every search found nothing for any format it needs; null when one found something.</param>
    /// <param name="now">The time now.</param>
    public static DateTimeOffset? NextSearch(DateTimeOffset? lastSearched, DateTimeOffset? missingSince, DateTimeOffset now) =>
        lastSearched is { } last
            ? last + (missingSince is { } since && now - since >= BackOffAfter ? Weekly : Daily)
            : null;

    /// <summary>Whether the book is due to be searched now.</summary>
    public static bool IsDue(DateTimeOffset? lastSearched, DateTimeOffset? missingSince, DateTimeOffset now) =>
        NextSearch(lastSearched, missingSince, now) is not { } next || next <= now;
}
