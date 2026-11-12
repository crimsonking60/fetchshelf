using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>A follow with its finds, newest first.</summary>
public sealed record FollowWithFinds(FollowEntity Follow, IReadOnlyList<FollowFindEntity> Finds)
{
    public int NewCount => Finds.Count(f => f.Status is FollowFindStatus.New or FollowFindStatus.Wanted);
}

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
    /// Finds waiting to be looked at: new ones, and wanted ones, which can still be downloaded at another cost. Finds of
    /// an ignored series are left out: the Following page doesn't list those.
    /// </summary>
    public static Task<int> WaitingCountAsync(FetchshelfDbContext dbContext, CancellationToken cancellationToken = default) =>
        (from find in dbContext.FollowFinds
         join follow in dbContext.Follows on find.FollowId equals follow.Id
         where follow.Mode != FollowMode.Ignore && (find.Status == FollowFindStatus.New || find.Status == FollowFindStatus.Wanted)
         select find.Id).CountAsync(cancellationToken);

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

    /// <summary>Every follow with its finds: followed series and authors by name, each one's finds newest first.</summary>
    public async Task<IReadOnlyList<FollowWithFinds>> AllAsync(CancellationToken cancellationToken = default)
    {
        var follows = await dbContext.Follows.Where(f => f.Mode != FollowMode.Ignore).ToListAsync(cancellationToken);
        var finds = (await dbContext.FollowFinds.ToListAsync(cancellationToken)).ToLookup(f => f.FollowId);
        return follows
            .OrderBy(f => f.Kind)
            .ThenBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(f => new FollowWithFinds(f, finds[f.Id].OrderByDescending(x => x.FoundAt.UtcTicks).ToList()))
            .ToList();
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
