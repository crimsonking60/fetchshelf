namespace Fetchshelf.Data.Entities;

/// <summary>
/// One request sent to MAM, kept for the MAM API page so the pattern of calls can be seen next to MAM's 429s.
/// Rows older than <see cref="Fetchshelf.Data.MamCalls.MamCallRecorder.DaysKept"/> days are deleted.
/// </summary>
public sealed class MamCallEntity
{
    public long Id { get; set; }

    /// <summary>When the request was sent, as Unix milliseconds, so SQLite compares and sorts it as a number.</summary>
    public long StartedAtMs { get; set; }

    /// <summary>How long until the whole answer was read, or the request failed, in milliseconds.</summary>
    public int DurationMs { get; set; }

    public required string Method { get; set; }

    public required string Host { get; set; }

    /// <summary>The path without the query, a download's token replaced by <c>&lt;hash&gt;</c>.</summary>
    public required string Path { get; set; }

    /// <summary>The HTTP status; null when there was no answer.</summary>
    public int? StatusCode { get; set; }

    /// <summary>Why there was no answer; null when there was one.</summary>
    public string? Failure { get; set; }

    /// <summary>Who held the MAM gate when the call was made, such as "Autograb Freeleech"; null when nobody did.</summary>
    public string? Caller { get; set; }
}
