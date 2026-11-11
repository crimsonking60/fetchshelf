using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>EF Core context for Fetchshelf's persistent store.</summary>
public sealed class FetchshelfDbContext(DbContextOptions<FetchshelfDbContext> options) : DbContext(options)
{
    public DbSet<TorrentEntity> Torrents => Set<TorrentEntity>();

    public DbSet<SelectedTorrentEntity> SelectedTorrents => Set<SelectedTorrentEntity>();

    public DbSet<DuplicateTorrentEntity> DuplicateTorrents => Set<DuplicateTorrentEntity>();

    public DbSet<ErroredTorrentEntity> ErroredTorrents => Set<ErroredTorrentEntity>();

    public DbSet<EventEntity> Events => Set<EventEntity>();

    public DbSet<ListEntity> Lists => Set<ListEntity>();

    public DbSet<ListItemEntity> ListItems => Set<ListItemEntity>();

    public DbSet<JobRunEntity> JobRuns => Set<JobRunEntity>();

    public DbSet<NotificationStateEntity> NotificationStates => Set<NotificationStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TorrentEntity>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.MamId);
            entity.HasIndex(t => t.NormalizedTitleKey);
        });

        modelBuilder.Entity<SelectedTorrentEntity>(entity =>
        {
            entity.HasIndex(e => e.MamId);
        });

        modelBuilder.Entity<ErroredTorrentEntity>(entity =>
        {
            entity.HasIndex(e => new { e.Stage, e.Key }).IsUnique();
        });

        modelBuilder.Entity<EventEntity>(entity =>
        {
            entity.HasIndex(e => e.TorrentId);
            entity.HasIndex(e => e.MamId);
        });

        modelBuilder.Entity<ListEntity>(entity =>
        {
            entity.HasKey(l => l.Id);
        });

        modelBuilder.Entity<ListItemEntity>(entity =>
        {
            entity.HasKey(i => new { i.ListId, i.ExternalId });
            entity.HasIndex(i => i.ListId);
        });

        modelBuilder.Entity<NotificationStateEntity>(entity =>
        {
            entity.HasKey(s => s.Key);
        });

        modelBuilder.Entity<JobRunEntity>(entity =>
        {
            entity.HasIndex(r => r.JobName);
        });
    }
}
