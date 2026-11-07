namespace Fetchshelf.Web.Pages;

/// <summary>
/// A Search result's Fetchshelf status badge. While the torrent is on its way into the library (selected,
/// downloading, or downloaded but not yet linked) the badge asks again every <see cref="PollSeconds"/> seconds, from Fetchshelf's
/// database only, so it turns into Linked without a reload. It stops once linked, or after <see cref="MaxRounds"/>
/// checks, so a forgotten tab doesn't ask forever.
/// </summary>
/// <param name="MamId">The torrent's MAM id.</param>
/// <param name="Status">Where the torrent stands in Fetchshelf.</param>
/// <param name="Round">How many times the badge has asked so far.</param>
public sealed record SearchResultStatusViewModel(long MamId, MamResultStatus Status, int Round = 0)
{
    public const int PollSeconds = 60;
    public const int MaxRounds = 60;

    /// <summary>Whether the badge asks again: the torrent isn't in the library yet, and the time isn't up.</summary>
    public bool Polls => Status is MamResultStatus.Selected or MamResultStatus.Downloading or MamResultStatus.Downloaded && Round < MaxRounds;
}
