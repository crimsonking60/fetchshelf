using System.Globalization;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Notifications;

/// <summary>The counts notifications compare against, kept in the NotificationStates table across restarts.</summary>
public sealed class NotificationStateStore(IDbContextFactory<FetchshelfDbContext> contextFactory, TimeProvider timeProvider)
{
    /// <summary>The count stored under <paramref name="key"/>; null when there is none.</summary>
    public async Task<long?> GetAsync(string key)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var state = await context.NotificationStates.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
        return state is not null && long.TryParse(state.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;
    }

    public async Task SetAsync(string key, long count)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var value = count.ToString(CultureInfo.InvariantCulture);
        var state = await context.NotificationStates.FirstOrDefaultAsync(s => s.Key == key);
        if (state is null)
        {
            context.NotificationStates.Add(new NotificationStateEntity { Key = key, Value = value, UpdatedAt = timeProvider.GetUtcNow() });
        }
        else
        {
            (state.Value, state.UpdatedAt) = (value, timeProvider.GetUtcNow());
        }

        await context.SaveChangesAsync();
    }
}
