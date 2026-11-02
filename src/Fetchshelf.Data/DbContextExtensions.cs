using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

public static class DbContextExtensions
{
    /// <summary>
    /// Drops whatever a failed step of a run left unsaved, so the next save neither stores it half done nor fails on
    /// it again. Unlike ChangeTracker.Clear, entities loaded earlier stay tracked, so the run's later steps that change
    /// them are still saved.
    /// </summary>
    public static void DiscardPendingChanges(this DbContext dbContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified or EntityState.Deleted:
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                    break;
            }
        }
    }
}
