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
    public static JobOutcome Link(int linked, int failed) => Finish(
        linked == 0 && failed == 0 ? "Nothing new to link" : Count(linked, "torrent", "torrents") + " linked",
        failed,
        "could not be linked; see the Errors page",
        new() { ["linked"] = linked, ["failed"] = failed });

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

    public static JobOutcome Snatchlist(SnatchlistRunSummary summary) => Finish(
        DryRun(
            summary.DryRun,
            summary.DryRun
                ? $"{Count(summary.Rows, "row", "rows")} read, {summary.Recorded} would have been recorded, {summary.Updated} would have been updated"
                : $"{Count(summary.Rows, "row", "rows")} read, {summary.Recorded} recorded, {summary.Updated} updated"),
        summary.Failed,
        "could not be imported; see the Errors page",
        new() { ["rows"] = summary.Rows, ["recorded"] = summary.Recorded, ["updated"] = summary.Updated, ["failed"] = summary.Failed });

    public static JobOutcome HardcoverList(HardcoverListRunSummary summary) => Finish(
        DryRun(
            summary.DryRun,
            $"{Count(summary.Books, "book", "books")}, {summary.Searched} searched on MAM, {summary.Selected} "
                + (summary.DryRun ? "would have been selected" : "selected")),
        summary.Failed,
        "could not be searched for; see the Errors page",
        new() { ["books"] = summary.Books, ["searched"] = summary.Searched, ["selected"] = summary.Selected, ["failed"] = summary.Failed });

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
