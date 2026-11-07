namespace Fetchshelf.Web.Pages;

/// <summary>
/// A Search result's Fetchshelf status badge. While the torrent is on its way into the library (selected,
/// downloading, or downloaded but not yet linked) the badge asks again: every 10 seconds for the first minute, when a
/// click has just selected it, then every minute. Each check reads Fetchshelf's database and, for a torrent still
/// downloading, asks qBittorrent about that one torrent. It stops once linked, or after about an hour, so a forgotten
/// tab doesn't ask forever.
/// </summary>
/// <param name="MamId">The torrent's MAM id.</param>
/// <param name="Status">Where the torrent stands in Fetchshelf.</param>
/// <param name="Round">How many times the badge has asked so far.</param>
public sealed record SearchResultStatusViewModel(long MamId, MamResultStatus Status, int Round = 0)
{
    public const int FastRounds = 6;
    public const int MaxRounds = FastRounds + 60;

    /// <summary>Seconds until the next check: short at first, then once a minute.</summary>
    public int DelaySeconds => Round < FastRounds ? 10 : 60;

    /// <summary>Whether the badge asks again: the torrent isn't in the library yet, and the time isn't up.</summary>
    public bool Polls => Status is MamResultStatus.Selected or MamResultStatus.Downloading or MamResultStatus.Downloaded && Round < MaxRounds;
}
