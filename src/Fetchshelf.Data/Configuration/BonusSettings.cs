using Fetchshelf.Core.AutoDownloader;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// The optional `[bonus]` section: the Bonus job, which spends the account's bonus points on wedges, VIP and upload
/// credit. Absent (null on <see cref="FetchshelfSettings.Bonus"/>) means nothing is ever bought.
/// </summary>
public sealed class BonusSettings
{
    /// <summary>
    /// `bonus_interval`: minutes between runs: 0 means only from the Jobs page, otherwise at least
    /// <see cref="MinIntervalMinutes"/>. Not the top-level `search_interval`, which is for jobs that search MAM.
    /// </summary>
    public int BonusInterval { get; set; } = 120;

    /// <summary>Upload credit and VIP are only bought with points above this; a wedge may use them.</summary>
    public long KeepPoints { get; set; }

    /// <summary>The GB one upload purchase buys, tried largest first; empty buys no upload credit.</summary>
    public List<int> UploadAmounts { get; set; } = [100, 50];

    /// <summary>Hours between wedge purchases; 0 buys none.</summary>
    public int WedgeEvery { get; set; }

    /// <summary>Whether VIP is topped up to the most MAM allows, once a run.</summary>
    public bool Vip { get; set; }

    public int MaxPurchasesPerRun { get; set; } = 10;

    public bool DryRun { get; set; }

    /// <summary>The shortest interval between runs: points come in slowly, and MAM refuses purchases that come too fast.</summary>
    public const int MinIntervalMinutes = 60;

    /// <summary>The most upload credit one purchase may buy, in GB.</summary>
    /// <summary>
    /// The GB of upload credit an automation may buy, the only amounts `upload_amounts` may hold. MAM sells 1, 5, 20, 50
    /// and 100 GB, but its rules for automated purchases allow only 50 and 100.
    /// </summary>
    public static readonly IReadOnlyList<int> UploadAmountsAllowed = [50, 100];

    /// <summary>The longest wait between two wedge purchases: a year.</summary>
    public const int MaxWedgeEveryHours = 8760;

    public const int MaxPurchasesPerRunLimit = 100;

    /// <summary>The settings as the planner decides by them.</summary>
    public BonusPolicy ToPolicy() => new(KeepPoints, UploadAmounts, WedgeEvery, Vip, MaxPurchasesPerRun);
}
