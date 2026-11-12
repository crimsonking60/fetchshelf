using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>A followed series or author, with how many of its finds are in each state, for the Following page's overview.</summary>
/// <param name="Follow">The series or author.</param>
/// <param name="ToLookAt">Finds waiting to be downloaded or dismissed, in a format a grab block takes; not-free ones included.</param>
/// <param name="NotFree">Of those, the ones no grab block's cost accepts.</param>
/// <param name="Selected">Finds selected for download, by the Follow job or by hand, and not linked yet.</param>
/// <param name="InLibrary">
/// Finds the library has by now: marked so by a search, or selected and linked since, which the next search marks.
/// </param>
public sealed record FollowSummary(FollowEntity Follow, int ToLookAt, int NotFree, int Selected, int InLibrary);

/// <summary>Which of a list's finds it shows.</summary>
public enum FindShow
{
    /// <summary>Waiting to be downloaded or dismissed, in a format a grab block takes (<see cref="FollowQueries.WantedSides"/>).</summary>
    ToLookAt,

    /// <summary>Waiting, and no grab block's cost accepts it.</summary>
    NotFree,

    Selected,

    InLibrary,

    Dismissed,

    All,
}

/// <summary>A find with the series or author it was found for.</summary>
public sealed record FindRow(FollowFindEntity Find, FollowEntity Follow);

/// <summary>What became of a Download click on a find.</summary>
/// <param name="Outcome">The selection's outcome; null when it never got that far.</param>
/// <param name="Metadata">The torrent's metadata as MAM has it now; null when MAM didn't return it.</param>
/// <param name="Problem">Why nothing was selected, when it never got to selection.</param>
public sealed record FindDownloadResult(SelectionOutcome? Outcome, TorrentMetadata? Metadata, string? Problem);

/// <summary>
/// What the pages and the menu read about follows. Static, as the Series page and the menu also run without a `[follow]`
/// section, when <see cref="FollowService"/> isn't there.
/// </summary>
public static class FollowQueries
{
    /// <summary>
    /// The formats ("Audio", "Ebook", as <see cref="FollowFindEntity.MediaSide"/> holds them) at least one grab block
    /// takes. A find waiting in another format was found before the blocks changed; it is no longer listed or counted,
    /// and comes back if a block takes its format again.
    /// </summary>
    public static IReadOnlySet<string> WantedSides(IReadOnlyList<GrabEntry> blocks) =>
        new[] { ("Audio", (Func<GrabEntry, CategoryListOrBool>)(b => b.Categories.Audio)), ("Ebook", b => b.Categories.Ebook) }
            .Where(side => blocks.Any(block => side.Item2(block).Names is not { Count: 0 }))
            .Select(side => side.Item1)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether a find can be left as not free: only a block with `cost = "free"` turns a torrent down for its cost, so
    /// with none, every match a block's filter takes is one it pays for.
    /// </summary>
    public static bool CanBeNotFree(IReadOnlyList<GrabEntry> blocks) => blocks.Any(block => block.Cost == ConfiguredCost.Free);

    /// <summary>
    /// Finds waiting to be looked at: new ones, and wanted ones, which can still be downloaded at another cost, in a
    /// format a grab block takes (<see cref="WantedSides"/>). Finds of an ignored series are left out: the Following page
    /// doesn't list those.
    /// </summary>
    public static Task<int> WaitingCountAsync(
        FetchshelfDbContext dbContext, IReadOnlySet<string> wantedSides, CancellationToken cancellationToken = default) =>
        (from find in dbContext.FollowFinds
         join follow in dbContext.Follows on find.FollowId equals follow.Id
         where follow.Mode != FollowMode.Ignore && (find.Status == FollowFindStatus.New || find.Status == FollowFindStatus.Wanted)
             && wantedSides.Contains(find.MediaSide)
         select find.Id).CountAsync(cancellationToken);

    /// <summary>
    /// Every followed series and author (not ignored ones), with its finds counted by state in the database, so the
    /// overview doesn't read the finds themselves.
    /// </summary>
    public static async Task<List<FollowSummary>> SummariesAsync(
        FetchshelfDbContext dbContext, IReadOnlySet<string> wantedSides, CancellationToken cancellationToken = default)
    {
        var follows = await dbContext.Follows.AsNoTracking().Where(f => f.Mode != FollowMode.Ignore).ToListAsync(cancellationToken);
        var counts = (await dbContext.FollowFinds
                .Select(f => new { f.FollowId, f.Status, f.MediaSide, Linked = dbContext.Torrents.Any(t => t.MamId == f.MamId && t.LibraryPath != null) })
                .GroupBy(f => new { f.FollowId, f.Status, f.MediaSide, f.Linked })
                .Select(g => new { g.Key.FollowId, g.Key.Status, g.Key.MediaSide, g.Key.Linked, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToLookup(c => c.FollowId);
        return follows.Select(follow =>
        {
            var own = counts[follow.Id].ToList();
            int Count(Func<FollowFindStatus, string, bool, bool> which) => own.Where(c => which(c.Status, c.MediaSide, c.Linked)).Sum(c => c.Count);
            return new FollowSummary(
                follow,
                ToLookAt: Count((status, side, _) => status is FollowFindStatus.New or FollowFindStatus.Wanted && wantedSides.Contains(side)),
                NotFree: Count((status, side, _) => status == FollowFindStatus.Wanted && wantedSides.Contains(side)),
                Selected: Count((status, _, linked) => status == FollowFindStatus.Selected && !linked),
                InLibrary: Count((status, _, linked) => status == FollowFindStatus.Owned || (status == FollowFindStatus.Selected && linked)));
        }).ToList();
    }

    /// <summary>
    /// The finds a list shows, with their series or author: of one follow, or of every followed one (not ignored ones),
    /// in one format or both. Unsorted; the page sorts and pages them.
    /// </summary>
    /// <param name="dbContext">Fetchshelf's database.</param>
    /// <param name="followId">One follow's finds; null for every follow's.</param>
    /// <param name="show">Which finds, by what became of them.</param>
    /// <param name="side">"Audio" or "Ebook" for one format; null for both.</param>
    /// <param name="wantedSides">The formats a grab block takes (<see cref="WantedSides"/>), which decide what is to look at.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public static async Task<List<FindRow>> FindsAsync(
        FetchshelfDbContext dbContext, int? followId, FindShow show, string? side, IReadOnlySet<string> wantedSides,
        CancellationToken cancellationToken = default)
    {
        var query = from find in dbContext.FollowFinds.AsNoTracking()
                    join follow in dbContext.Follows.AsNoTracking() on find.FollowId equals follow.Id
                    where follow.Mode != FollowMode.Ignore
                    select new { find, follow, linked = dbContext.Torrents.Any(t => t.MamId == find.MamId && t.LibraryPath != null) };
        if (followId is { } id)
        {
            query = query.Where(row => row.follow.Id == id);
        }

        if (side is not null)
        {
            query = query.Where(row => row.find.MediaSide == side);
        }

        var sides = wantedSides.ToList();
        query = show switch
        {
            FindShow.ToLookAt => query.Where(row => (row.find.Status == FollowFindStatus.New || row.find.Status == FollowFindStatus.Wanted)
                && sides.Contains(row.find.MediaSide)),
            FindShow.NotFree => query.Where(row => row.find.Status == FollowFindStatus.Wanted && sides.Contains(row.find.MediaSide)),
            FindShow.Selected => query.Where(row => row.find.Status == FollowFindStatus.Selected && !row.linked),
            FindShow.InLibrary => query.Where(row => row.find.Status == FollowFindStatus.Owned
                || (row.find.Status == FollowFindStatus.Selected && row.linked)),
            FindShow.Dismissed => query.Where(row => row.find.Status == FollowFindStatus.Dismissed),
            _ => query,
        };
        return (await query.ToListAsync(cancellationToken)).Select(row => new FindRow(row.find, row.follow)).ToList();
    }

    /// <summary>The follow of every followed, watched or ignored series, by key; for the Series page.</summary>
    public static async Task<Dictionary<string, FollowEntity>> SeriesFollowsAsync(FetchshelfDbContext dbContext, CancellationToken cancellationToken = default) =>
        await dbContext.Follows.AsNoTracking().Where(f => f.Kind == FollowKind.Series)
            .ToDictionaryAsync(f => f.NameKey, StringComparer.Ordinal, cancellationToken);
}

/// <summary>The follows as the pages change them: following, the mode, stopping, finds put aside or downloaded.</summary>
public sealed class FollowService(
    FetchshelfDbContext dbContext, MamClient mamClient, MamAccessGate accessGate, SelectionRunner selectionRunner, FollowSettings settings,
    TimeProvider? timeProvider = null)
{
    /// <summary>How long a Download click waits for a running MAM job before saying MAM is busy.</summary>
    public static readonly TimeSpan GateWait = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// What the Follow button on a torrent's page or the Torrents page starts a follow in: listing what is found
    /// (*Follow*), for a series as for an author, so the button's word is the mode it sets. *Auto* is chosen on the Series
    /// or Following page.
    /// </summary>
    public const FollowMode DefaultMode = FollowMode.Watch;

    public Task<FollowEntity?> FindAsync(FollowKind kind, string nameKey, CancellationToken cancellationToken = default) =>
        dbContext.Follows.FirstOrDefaultAsync(f => f.Kind == kind && f.NameKey == nameKey, cancellationToken);

    /// <summary>
    /// Follows a series or author in <paramref name="mode"/>, or <see cref="DefaultMode"/>, or changes the mode of one already
    /// followed. An author can't be ignored: ignoring only hides a series from those with missing books.
    /// </summary>
    public async Task<FollowEntity> SetAsync(
        FollowKind kind, string nameKey, string displayName, FollowMode? mode = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nameKey))
        {
            throw new ArgumentException("A follow needs a name key.", nameof(nameKey));
        }

        if (kind == FollowKind.Author && mode == FollowMode.Ignore)
        {
            throw new ArgumentException("Only a series can be ignored.", nameof(mode));
        }

        var follow = await FindAsync(kind, nameKey, cancellationToken);
        if (follow is null)
        {
            follow = new FollowEntity
            {
                Kind = kind,
                NameKey = nameKey,
                DisplayName = displayName.Trim(),
                Mode = mode ?? DefaultMode,
                CreatedAt = _time.GetUtcNow(),
            };
            dbContext.Follows.Add(follow);
        }
        else if (mode is { } newMode)
        {
            follow.Mode = newMode;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return follow;
    }

    /// <summary>
    /// Stops following: the follow, its finds and its error on the Errors page go, as no run will clear that any more.
    /// Nothing already selected is touched.
    /// </summary>
    public async Task StopAsync(int followId, CancellationToken cancellationToken = default)
    {
        await dbContext.Follows.Where(f => f.Id == followId).ExecuteDeleteAsync(cancellationToken);
        await ErrorLog.ClearAsync(dbContext, ErrorStage.Follow, ErrorLog.FollowKey(followId), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Makes the follow the first one the next Follow run searches, whenever it was searched last.</summary>
    public async Task SearchSoonAsync(int followId, CancellationToken cancellationToken = default)
    {
        var follow = await dbContext.Follows.FirstOrDefaultAsync(f => f.Id == followId, cancellationToken);
        if (follow is not null)
        {
            follow.LastSearchedAt = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DismissAsync(int findId, CancellationToken cancellationToken = default)
    {
        var find = await dbContext.FollowFinds.FirstOrDefaultAsync(f => f.Id == findId, cancellationToken);
        if (find is not null)
        {
            (find.Status, find.StatusAt) = (FollowFindStatus.Dismissed, _time.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Selects a find's torrent as the Search page's Download does: MAM is asked for it again, so the cost is today's,
    /// and it goes through the usual selection rules, with the `[follow]` category.
    /// </summary>
    public async Task<FindDownloadResult> DownloadAsync(int findId, bool wedge, CancellationToken cancellationToken = default)
    {
        var find = await dbContext.FollowFinds.FirstOrDefaultAsync(f => f.Id == findId, cancellationToken);
        var follow = find is null ? null : await dbContext.Follows.FirstOrDefaultAsync(f => f.Id == find.FollowId, cancellationToken);
        if (find is null || follow is null)
        {
            return new FindDownloadResult(null, null, "That find is no longer listed.");
        }

        using var gate = await accessGate.TryAcquireAsync("Following page", GateWait, cancellationToken);
        if (gate is null)
        {
            return new FindDownloadResult(null, null, $"MAM is busy with {accessGate.Owner ?? "another job"}; try again in a moment.");
        }

        var lookup = await mamClient.SearchAsync(new MamSearchQuery { Id = find.MamId, ReturnDownloadLink = true }, cancellationToken);
        if (lookup.Data.FirstOrDefault() is not { } record)
        {
            return new FindDownloadResult(null, null, $"MAM returned no torrent with id {find.MamId}.");
        }

        var metadata = MamTorrentMapper.Map(record);
        var outcome = await selectionRunner.ProcessCandidateAsync(
            record, metadata, [], wedge ? ConfiguredCost.Wedge : ConfiguredCost.Ratio, autograbCategory: settings.Category,
            grabberName: FollowRunner.GrabberName(follow), cancellationToken: cancellationToken);
        var status = outcome switch
        {
            SelectionOutcome.Selected or SelectionOutcome.SelectedAsUpgrade or SelectionOutcome.DuplicateOfSelected or SelectionOutcome.RefreshedSelected => FollowFindStatus.Selected,
            SelectionOutcome.DuplicateOfLibrary or SelectionOutcome.RefreshedLibrary => FollowFindStatus.Owned,
            _ => find.Status,
        };
        if (status != find.Status)
        {
            (find.Status, find.StatusAt) = (status, _time.GetUtcNow());
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        return new FindDownloadResult(outcome, metadata, null);
    }
}
