namespace Fetchshelf.Clients.Mam;

/// <summary>
/// The account's numbers from MAM's `jsonLoad.php`. Ratio, the upload and download totals, wedges and the
/// unsatisfied-snatch count and limit are required: every safety rule works from them. The rest is shown on the
/// Account page, and read only when MAM sends it, so a field MAM drops or renames doesn't break the account read.
/// </summary>
public sealed record MamAccountState
{
    public required double Ratio { get; init; }

    public required double UploadedBytes { get; init; }

    public required double DownloadedBytes { get; init; }

    public required long Wedges { get; init; }

    public required long Uid { get; init; }

    public required long UnsatCount { get; init; }

    public required long UnsatLimit { get; init; }

    public string? Username { get; init; }

    /// <summary>The account's class, such as "Power User" or "VIP".</summary>
    public string? ClassName { get; init; }

    public double? BonusPoints { get; init; }

    /// <summary>Whether MAM can reach the client, as MAM words it, such as "yes".</summary>
    public string? Connectable { get; init; }

    /// <summary>Every kind of snatch MAM counts in its snatch summary, in the order MAM gives them.</summary>
    public IReadOnlyList<MamSnatchKind> SnatchKinds { get; init; } = [];
}

/// <param name="Key">MAM's key for the kind, such as <c>inactHnr</c>; it, not the label, decides what needs attention.</param>
/// <param name="Name">MAM's own label, such as "Not Seeding - H&amp;R - Not Yet Satisfied".</param>
/// <param name="Count">How many snatches are of this kind.</param>
/// <param name="SizeBytes">Their total size; null when MAM gives none.</param>
public sealed record MamSnatchKind(string Key, string Name, long Count, long? SizeBytes);
