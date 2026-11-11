using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>
/// Keeps at most one error row per (stage, key): a repeat failure updates the existing row, and a later success
/// clears it. Neither method saves; the caller's SaveChanges does.
/// </summary>
public static class ErrorLog
{
    /// <summary>The key of a Hardcover list book's error: the list's id and the book's Hardcover id, joined with a space.</summary>
    public static string ListItemKey(string listId, string externalId) => $"{listId} {externalId}";

    /// <summary>The keys that have an error row at <paramref name="stage"/>, so a run clears only the rows there are.</summary>
    public static async Task<HashSet<string>> KeysAsync(FetchshelfDbContext dbContext, ErrorStage stage, CancellationToken cancellationToken = default) =>
        (await dbContext.ErroredTorrents.Where(e => e.Stage == stage).Select(e => e.Key).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

    public static async Task RecordAsync(
        FetchshelfDbContext dbContext,
        ErrorStage stage,
        string key,
        string? title,
        string errorText,
        string? displayMetadataJson = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindAsync(dbContext, stage, key, cancellationToken);
        if (existing is null)
        {
            dbContext.ErroredTorrents.Add(new ErroredTorrentEntity
            {
                Stage = stage,
                Key = key,
                Title = title,
                ErrorText = errorText,
                DisplayMetadataJson = displayMetadataJson,
                OccurredAt = DateTimeOffset.UtcNow,
            });
            return;
        }

        existing.Title = title ?? existing.Title;
        existing.ErrorText = errorText;
        existing.DisplayMetadataJson = displayMetadataJson;
        existing.OccurredAt = DateTimeOffset.UtcNow;
    }

    public static async Task ClearAsync(FetchshelfDbContext dbContext, ErrorStage stage, string key, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(dbContext, stage, key, cancellationToken) is { } existing)
        {
            dbContext.ErroredTorrents.Remove(existing);
        }
    }

    private static async Task<ErroredTorrentEntity?> FindAsync(
        FetchshelfDbContext dbContext, ErrorStage stage, string key, CancellationToken cancellationToken) =>
        dbContext.ErroredTorrents.Local.FirstOrDefault(e => e.Stage == stage && e.Key == key && dbContext.Entry(e).State != EntityState.Deleted)
        ?? await dbContext.ErroredTorrents.FirstOrDefaultAsync(e => e.Stage == stage && e.Key == key, cancellationToken);
}
