using System.Globalization;
using System.Text.RegularExpressions;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Account;

/// <summary>One number Fetchshelf works out from the account, with the formula it came from in words.</summary>
/// <param name="Label">What the number is for: "Everything else", or an autograb with buffers of its own.</param>
/// <param name="Value">The number, as shown.</param>
/// <param name="Formula">How it was worked out.</param>
public sealed record DerivedNumber(string Label, string Value, string Formula);

/// <summary>One kind of snatch from MAM's snatch summary, as the page lists it.</summary>
public sealed record SnatchRow(string Key, string Label, long Count, string Size);

/// <summary>Why MAM calls are paused, until when, and what to do about it.</summary>
public sealed record PauseNotice(DateTimeOffset Until, string Reason, string Advice);

/// <summary>Everything the Account page and the Jobs page's strip show, from the last account read and the settings.</summary>
public sealed record AccountSummary
{
    /// <summary>The last account read, or null when nothing has been read since Fetchshelf started.</summary>
    public MamAccountSnapshot? Snapshot { get; init; }

    /// <summary>Where the account's snatch lists are on MAM; null without a read.</summary>
    public string? MamProfileUrl { get; init; }

    public IReadOnlyList<SnatchRow> Snatches { get; init; } = [];

    /// <summary>The snatch kinds that need attention and have any: hit-and-runs, unsatisfied ones not seeding, tracker errors.</summary>
    public IReadOnlyList<SnatchRow> Warnings { get; init; } = [];

    public DerivedNumber? RatioBudget { get; init; }

    public IReadOnlyList<DerivedNumber> SelectionSlots { get; init; } = [];

    public IReadOnlyList<DerivedNumber> Wedges { get; init; } = [];

    /// <summary>Selections waiting on the Selected page.</summary>
    public int Queued { get; init; }

    /// <summary>How many of <see cref="Queued"/> the current numbers would let the Download job start.</summary>
    public int Startable { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public PauseNotice? Pause { get; init; }

    public MamRefusal? LastRefusal { get; init; }
}

/// <summary>Builds the <see cref="AccountSummary"/> without calling MAM: from what the jobs last read, the settings and the queue.</summary>
public sealed partial class AccountSummaryBuilder(
    MamClient mamClient, MamClientOptions mamOptions, FetchshelfSettings settings, SafetyBuffers safetyBuffers, FetchshelfDbContext dbContext)
{
    /// <summary>The kinds of snatch that need attention when there are any. Decided by key, so a reworded label doesn't silence one.</summary>
    public static readonly IReadOnlyList<string> AttentionKeys = ["inactHnr", "inactUnsat", "ite"];

    public async Task<AccountSummary> BuildAsync(CancellationToken cancellationToken = default)
    {
        var connection = new AccountSummary
        {
            LastSuccessAt = mamClient.LastSuccessAt,
            Pause = DescribePause(mamClient.PausedUntil, mamClient.PauseReason),
            LastRefusal = mamClient.LastRefusal,
        };
        if (mamClient.LastAccountState is not { } snapshot)
        {
            return connection;
        }

        var account = snapshot.State;
        var inFlight = await SelectionQueue.InFlightBytesAsync(dbContext, cancellationToken);
        var ratioBudgetBytes = DownloadBudget.ComputeRatioBudgetBytes(account.UploadedBytes, account.DownloadedBytes, inFlight, settings.MinRatio);
        var pending = await SelectionQueue.PendingAsync(dbContext, cancellationToken);
        var queue = pending.Select(s => new QueuedTorrent(
            Enum.Parse<SelectedTorrentCost>(s.CostDecision), TorrentMetadataSerializer.Deserialize(s.MetadataJson).TotalSizeBytes, s.GrabberName));
        var startable = DownloadQueueForecast.CountStartable(
            queue, new DownloadRunBudget(account.UnsatLimit, account.UnsatCount, account.Wedges, ratioBudgetBytes), safetyBuffers);
        var snatches = account.SnatchKinds
            .Select(k => new SnatchRow(k.Key, CollapseSpaces(k.Name), k.Count, k.SizeBytes is { } size ? DownloadBudget.FormatBytes(size) : "-"))
            .ToList();

        return connection with
        {
            Snapshot = snapshot,
            MamProfileUrl = $"{mamOptions.BaseUrl.TrimEnd('/')}/u/{account.Uid.ToString(CultureInfo.InvariantCulture)}",
            Snatches = snatches,
            Warnings = AttentionKeys.Select(key => snatches.FirstOrDefault(s => s.Key == key)).OfType<SnatchRow>().Where(s => s.Count > 0).ToList(),
            RatioBudget = DescribeRatioBudget(account, inFlight, ratioBudgetBytes),
            SelectionSlots = await DescribeSelectionSlotsAsync(account, cancellationToken),
            Wedges = DescribeWedges(account),
            Queued = pending.Count,
            Startable = startable,
        };
    }

    private DerivedNumber DescribeRatioBudget(MamAccountState account, long inFlight, long budget)
    {
        var minRatio = settings.MinRatio.ToString(CultureInfo.InvariantCulture);
        var formula = $"uploaded {DownloadBudget.FormatBytes(account.UploadedBytes)} / min_ratio {minRatio} - downloaded "
            + $"{DownloadBudget.FormatBytes(account.DownloadedBytes)} - {DownloadBudget.FormatBytes(inFlight)} started but not finished";
        return budget == 0
            ? new DerivedNumber("Ratio budget", "0 B", $"The ratio is at or below min_ratio {minRatio}: {formula}")
            : new DerivedNumber("Ratio budget", DownloadBudget.FormatBytes(budget), formula);
    }

    private async Task<List<DerivedNumber>> DescribeSelectionSlotsAsync(MamAccountState account, CancellationToken cancellationToken)
    {
        var lines = new List<DerivedNumber> { SelectionSlots("Everything else", settings.UnsatBuffer, maxActive: null, active: 0) };
        for (var i = 0; i < settings.Autograb.Count; i++)
        {
            var entry = settings.Autograb[i];
            if (entry.UnsatBuffer is null && entry.MaxActiveDownloads is null)
            {
                continue;
            }

            var active = entry.MaxActiveDownloads is null
                ? 0
                : await dbContext.SelectedTorrents.CountAsync(
                    s => s.GrabberName == entry.Name && s.RemovedAt == null && s.StartedAt == null, cancellationToken);
            lines.Add(SelectionSlots(
                $"Autograb {AutoDownloaderJobNames.Autograb(entry, i)}", entry.UnsatBuffer ?? settings.UnsatBuffer, entry.MaxActiveDownloads, active));
        }

        return lines;

        DerivedNumber SelectionSlots(string label, int unsatBuffer, int? maxActive, int active) => new(
            label,
            SelectionBudget.Compute(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActive, active).ToString("N0", CultureInfo.InvariantCulture),
            SelectionBudget.Explain(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActive, active));
    }

    private List<DerivedNumber> DescribeWedges(MamAccountState account)
    {
        var lines = new List<DerivedNumber> { Wedges("Everything else", settings.WedgeBuffer) };
        for (var i = 0; i < settings.Autograb.Count; i++)
        {
            if (settings.Autograb[i].WedgeBuffer is { } buffer)
            {
                lines.Add(Wedges($"Autograb {AutoDownloaderJobNames.Autograb(settings.Autograb[i], i)}", buffer));
            }
        }

        return lines;

        DerivedNumber Wedges(string label, int buffer) => new(
            label,
            Math.Max(0, account.Wedges - buffer).ToString("N0", CultureInfo.InvariantCulture),
            $"{account.Wedges} wedges - wedge_buffer {buffer}");
    }

    private static PauseNotice? DescribePause(DateTimeOffset? until, MamPauseReason? reason) => (until, reason) switch
    {
        ({ } at, MamPauseReason.Forbidden) => new PauseNotice(
            at,
            "MAM refused mam_id (HTTP 403).",
            "The mam_id may have been invalidated or be locked to a different IP address, or the account may be blocked. "
                + "Fix mam_id in config.toml and restart Fetchshelf to resume sooner."),
        ({ } at, MamPauseReason.RateLimited) => new PauseNotice(
            at,
            "MAM asked Fetchshelf to slow down (HTTP 429).",
            "Calls resume by themselves when the pause ends."),
        _ => null,
    };

    /// <summary>MAM's labels sometimes hold runs of spaces ("Seeding -  Uploads").</summary>
    public static string CollapseSpaces(string label) => Spaces().Replace(label.Trim(), " ");

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}
