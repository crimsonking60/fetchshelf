namespace Fetchshelf.Data.Entities;

/// <summary>
/// The account as MAM last answered on one UTC day: the day's last read, kept so the Account page can show how the
/// ratio, the upload and the bonus points change over time.
/// </summary>
public sealed class AccountSnapshotEntity
{
    /// <summary>The UTC day, <c>yyyy-MM-dd</c>, so text order is day order.</summary>
    public required string Day { get; set; }

    /// <summary>When MAM answered the read this row holds.</summary>
    public required DateTimeOffset TakenAt { get; set; }

    public double Ratio { get; set; }

    public long UploadedBytes { get; set; }

    public long DownloadedBytes { get; set; }

    public long Wedges { get; set; }

    public long UnsatCount { get; set; }

    public long UnsatLimit { get; set; }

    public double? BonusPoints { get; set; }

    public string? ClassName { get; set; }
}
