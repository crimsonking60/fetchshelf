namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What happened to one candidate processed by <see cref="SelectionRunner"/>.</summary>
public enum SelectionOutcome
{
    Ignored,
    RefreshedSelected,
    RefreshedLibrary,
    MetadataOnlyCreated,
    MetadataOnlyStop,
    NoPreferredFormat,
    DuplicateOfSelected,
    DuplicateOfLibrary,
    CostNotFree,
    Selected,
}

public static class SelectionOutcomeDescriptions
{
    /// <summary>
    /// "Selected" only means queued: ratio, unsatisfied-snatch and wedge limits are checked when the downloader
    /// starts it.
    /// </summary>
    public static string Describe(this SelectionOutcome outcome, bool dryRun) => (outcome, dryRun) switch
    {
        (SelectionOutcome.Selected, true) => "would be selected; the downloader still checks ratio, unsat slots and wedges before starting it",
        (SelectionOutcome.Selected, false) => "selected",
        (SelectionOutcome.Ignored, _) => "in ignore_torrents, skipped",
        (SelectionOutcome.RefreshedSelected, _) => "already selected",
        (SelectionOutcome.RefreshedLibrary, _) => "already in the library",
        (SelectionOutcome.MetadataOnlyCreated, true) => "would be recorded as owned (metadata only)",
        (SelectionOutcome.MetadataOnlyCreated, false) => "recorded as owned (metadata only)",
        (SelectionOutcome.MetadataOnlyStop, _) => "not downloaded (cost is metadata_only)",
        (SelectionOutcome.NoPreferredFormat, _) => "none of its file types is in the format preference list, skipped",
        (SelectionOutcome.DuplicateOfSelected, _) => "duplicate of an equal or better selected torrent, skipped",
        (SelectionOutcome.DuplicateOfLibrary, _) => "duplicate of an equal or better torrent in the library, skipped",
        (SelectionOutcome.CostNotFree, _) => "not free, and the cost only allows free torrents, skipped",
        _ => outcome.ToString(),
    };
}
