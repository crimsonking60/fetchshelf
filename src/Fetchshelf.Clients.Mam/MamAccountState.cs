namespace Fetchshelf.Clients.Mam;

/// <summary>The account state requires: ratio, upload/download totals, wedges, and unsat count/limit.</summary>
public sealed record MamAccountState
{
    public required double Ratio { get; init; }

    public required double UploadedBytes { get; init; }

    public required double DownloadedBytes { get; init; }

    public required long Wedges { get; init; }

    public required long Uid { get; init; }

    public required long UnsatCount { get; init; }

    public required long UnsatLimit { get; init; }
}
