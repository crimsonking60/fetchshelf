using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>
/// Append-only access to the Event log. Deliberately exposes no update operation -
/// once written, an event's stored fields are never changed.
/// </summary>
public sealed class EventStore(FetchshelfDbContext dbContext)
{
    public async Task AppendAsync(EventEntity @event, CancellationToken cancellationToken = default)
    {
        dbContext.Events.Add(@event);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
