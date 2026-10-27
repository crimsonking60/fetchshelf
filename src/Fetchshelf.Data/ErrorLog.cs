using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>
/// Keeps at most one error row per (stage, key): a repeat failure updates the existing row, and a later success
/// clears it. Neither method saves; the caller's SaveChanges does.
/// </summary>
public static class ErrorLog
{
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
