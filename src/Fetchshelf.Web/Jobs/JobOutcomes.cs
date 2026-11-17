using Fetchshelf.Core.Jobs;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Web.Jobs;

/// <summary>
/// Turns each runner's summary into the job's outcome: one line for the Jobs page and its counts by name. A run where
/// some items failed is thrown as a <see cref="JobCompletedWithErrorsException"/> carrying the same line, so the
/// registry records it as finished with errors.
/// </summary>
public static class JobOutcomes
{
    /// <summary>
    /// What the Link job linked, and the backlog it left: torrents beyond the run's MAM lookups, and those MAM didn't
    /// know, which wait a day. A run with only a backlog isn't "Nothing new to link".
    /// </summary>
    public static JobOutcome Link(LinkRunSummary summary)
    {
        var backlog = summary.WaitingForLookup > 0 || summary.WaitingForMam > 0;
        var line = summary.Linked > 0 || summary.Failed > 0 ? Count(summary.Linked, "torrent", "torrents") + " linked"
            : backlog ? "Nothing linked"
            : "Nothing new to link";
        if (summary.WaitingForLookup > 0)
        {
            line += $"; {summary.WaitingForLookup} more to look up on MAM in the next runs";
        }

        if (summary.WaitingForMam > 0)
        {
            line += $"; {Count(summary.WaitingForMam, "torrent", "torrents")} MAM didn't know, looked up again once a day";
        }

        return Finish(
            line,
            summary.Failed,
            "could not be linked; see the Errors page",
            new()
            {
                ["linked"] = summary.Linked,
                ["failed"] = summary.Failed,
                ["waiting_for_lookup"] = summary.WaitingForLookup,
                ["waiting_for_mam"] = summary.WaitingForMam,
            });
    }

    public static JobOutcome Clean(CleanRunSummary summary) => Finish(
        summary.Retired == 0 && summary.Failed == 0
            ? "No superseded copies to retire"
            : Count(summary.Retired, "superseded copy", "superseded copies") + " retired",
        summary.Failed,
        "could not be retired; see the Errors page",
        new() { ["retired"] = summary.Retired, ["failed"] = summary.Failed });

    public static JobOutcome Audiobookshelf(AbsMatchSummary summary)
    {
        var line = summary is { Matched: 0, NotFoundYet: 0, Failed: 0 }
            ? "No linked books waiting to be matched"
            : Count(summary.Matched, "book", "books") + " matched"
                + (summary.NotFoundYet > 0 ? $", {summary.NotFoundYet} not in Audiobookshelf yet" : "");
        return Finish(
            line,
            summary.Failed,
            "could not be matched; see the Errors page",
            new() { ["matched"] = summary.Matched, ["not_found_yet"] = summary.NotFoundYet, ["failed"] = summary.Failed });
    }

    public static JobOutcome Autograb(AutograbRunSummary summary)
    {
        var counts = new Dictionary<string, long> { ["results"] = summary.ResultCount, ["selected"] = summary.Selected };
        if (summary.ResultCount == 0 && summary.Note is { } nothingSearched)
        {
            return new JobOutcome($"Nothing to do: {nothingSearched}", counts);
        }

        var line = $"{Count(summary.ResultCount, "search result", "search results")}, {summary.Selected} "
            + (summary.DryRun ? "would have been selected" : "selected")
            + (summary.Note is { } note ? $"; {note}" : "");

        if (summary.CheckedForRemoval > 0)
        {
            line += $"; {summary.CheckedForRemoval} checked for removal from MAM, {summary.MarkedRemoved} "
                + (summary.DryRun ? "would have been marked removed" : "marked removed");
            counts["checked_for_removal"] = summary.CheckedForRemoval;
            counts["marked_removed"] = summary.MarkedRemoved;
        }

        return new JobOutcome(DryRun(summary.DryRun, line), counts);
    }

    public static JobOutcome List(ListRunSummary summary)
    {
        var counts = new Dictionary<string, long>
        {
            ["books"] = summary.Books,
            ["searched"] = summary.Searched,
            ["selected"] = summary.Selected,
            ["failed"] = summary.Failed,
        };
        var unreadable = string.Empty;
        if (summary.UnreadableLines > 0)
        {
            // Lines of a list file: the log names them.
            unreadable = $"; {Count(summary.UnreadableLines, "line", "lines")} of the file could not be read, see the log";
            counts["unreadable_lines"] = summary.UnreadableLines;
        }

        return Finish(
            DryRun(
                summary.DryRun,
                $"{Count(summary.Books, "book", "books")}, {summary.Searched} searched on MAM, {summary.Selected} "
                    + (summary.DryRun ? "would have been selected" : "selected") + unreadable),
            summary.Failed,
            "could not be searched for; see the Errors page",
            counts);
    }

    public static JobOutcome Follow(FollowRunSummary summary) => Finish(
        DryRun(
            summary.DryRun,
            $"{summary.Searched} series and authors searched, {summary.Selected} "
                + (summary.DryRun ? "would have been selected" : "selected")
                + $", {summary.NewFinds} new to look at"),
        summary.Failed,
        "could not be searched for; see the Errors page",
        new() { ["searched"] = summary.Searched, ["selected"] = summary.Selected, ["new"] = summary.NewFinds, ["failed"] = summary.Failed });

    public static JobOutcome Upgrade(UpgradeRunSummary summary) => Finish(
        DryRun(
            summary.DryRun,
            summary is { Searched: 0, Note: { } note }
                ? $"Nothing searched: {note}"
                : $"{Count(summary.Searched, "book", "books")} searched"
                    + (summary.Searched > 0 ? $" ({summary.TitlesText})" : "")
                    + $", {Count(summary.Selected, "better copy", "better copies")} "
                    + (summary.DryRun ? "would have been selected" : "selected")),
        summary.Failed,
        "could not be searched for; see the Errors page",
        new() { ["searched"] = summary.Searched, ["selected"] = summary.Selected, ["failed"] = summary.Failed });

    public static JobOutcome Bonus(BonusRunSummary summary)
    {
        var line = DryRun(summary.DryRun, summary.Bought.Count == 0
            ? $"Nothing bought: {summary.Failure ?? summary.StopReason}"
            : $"{(summary.DryRun ? "Would buy" : "Bought")} {string.Join(", ", summary.Bought)}");
        var counts = new Dictionary<string, long> { ["bought"] = summary.DryRun ? 0 : summary.Bought.Count };
        return summary.Failure switch
        {
            null => new JobOutcome(line, counts),
            not null when summary.Bought.Count == 0 => throw new JobCompletedWithErrorsException(line, counts),
            { } failure => throw new JobCompletedWithErrorsException($"{line}; then {failure}", counts),
        };
    }

    public static JobOutcome Seeding(SeedingRunSummary summary)
    {
        var line = $"{Count(summary.Checked, "torrent", "torrents")} checked: {summary.LookUnsatisfied} {(summary.LookUnsatisfied == 1 ? "looks" : "look")} unsatisfied"
            + (summary.NotSeeding > 0 ? $" ({summary.NotSeeding} not seeding)" : "")
            + (summary.PastThePeriod > 0 ? $", {summary.PastThePeriod} past the period" : "")
            + (summary.NewlySatisfied > 0 ? $", {summary.NewlySatisfied} newly satisfied" : "")
            + (summary.OnCleanedApplied > 0 ? $", on_cleaned applied to {summary.OnCleanedApplied}" : "");
        return new JobOutcome(line, new Dictionary<string, long>
        {
            ["checked"] = summary.Checked,
            ["unsatisfied"] = summary.LookUnsatisfied,
            ["not_seeding"] = summary.NotSeeding,
            ["past_the_period"] = summary.PastThePeriod,
            ["newly_satisfied"] = summary.NewlySatisfied,
            ["on_cleaned_applied"] = summary.OnCleanedApplied,
        });
    }

    public static JobOutcome Backup(Data.DatabaseBackupRunSummary summary) => new(
        $"Backed up {Core.AutoDownloader.DownloadBudget.FormatBytes(summary.Backup.SizeBytes)} to {Path.GetFileName(summary.Backup.Path)}; "
            + $"{summary.Kept} kept",
        new Dictionary<string, long> { ["bytes"] = summary.Backup.SizeBytes, ["kept"] = summary.Kept });

    public static JobOutcome Download(DownloadRunSummary summary) => Finish(
        summary.Queued == 0
            ? "No selected torrents waiting to be downloaded"
            : $"{summary.Started} of {Count(summary.Queued, "queued torrent", "queued torrents")} added to qBittorrent"
                + (summary.LeftQueued > 0 ? $"; {summary.LeftQueued} stay queued: {summary.LimitedBy}" : ""),
        summary.Failed,
        "could not be downloaded; see the log",
        new() { ["queued"] = summary.Queued, ["started"] = summary.Started, ["failed"] = summary.Failed, ["left_queued"] = summary.LeftQueued });

    /// <summary>The outcome, or with failures the same line and counts thrown as a run that finished with errors.</summary>
    private static JobOutcome Finish(string line, int failed, string failedText, Dictionary<string, long> counts)
    {
        if (failed > 0)
        {
            throw new JobCompletedWithErrorsException($"{line}; {failed} {failedText}", counts);
        }

        return new JobOutcome(line, counts);
    }

    private static string DryRun(bool dryRun, string line) => dryRun ? $"Dry run: {line}" : line;

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";
}
