namespace Fetchshelf.Clients.Mam;

/// <summary>The account's numbers as MAM gave them, and when.</summary>
public sealed record MamAccountSnapshot(MamAccountState State, DateTimeOffset FetchedAt);

/// <summary>Why MAM calls are paused.</summary>
public enum MamPauseReason
{
    /// <summary>MAM refused `mam_id` (HTTP 403).</summary>
    Forbidden,

    /// <summary>MAM asked Fetchshelf to slow down (HTTP 429).</summary>
    RateLimited,
}

/// <summary>A time MAM refused the session or the account, and what it meant.</summary>
public sealed record MamRefusal(DateTimeOffset At, string Message);
