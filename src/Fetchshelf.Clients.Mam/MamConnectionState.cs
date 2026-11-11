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

/// <summary>What MAM answered that stops or slows calls.</summary>
public enum MamRefusalKind
{
    /// <summary>MAM refused `mam_id` (HTTP 403), and calls are paused.</summary>
    Forbidden,

    /// <summary>MAM blocked the account (HTTP 406). Nothing is paused: every call fails until MAM lifts the block.</summary>
    Blocked,

    /// <summary>MAM asked for fewer requests (HTTP 429); the next call waits out a minute.</summary>
    RateLimited,
}

/// <summary>Raised by <see cref="MamClient.Refused"/>.</summary>
/// <param name="Kind">What MAM answered.</param>
/// <param name="At">When.</param>
/// <param name="PausedUntil">Until when calls are paused or wait; null for a block, which pauses nothing.</param>
public sealed record MamRefusalEvent(MamRefusalKind Kind, DateTimeOffset At, DateTimeOffset? PausedUntil);
