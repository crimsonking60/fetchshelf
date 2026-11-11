using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>
/// EF Core context for Fetchshelf's persistent store. Every save keeps a torrent's people and series rows and its
/// language and media type columns in step with its metadata JSON (<see cref="TorrentProjectionSync"/>), whichever
/// page or job changed it.
/// </summary>
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

    public DbSet<TorrentPersonEntity> TorrentPeople => Set<TorrentPersonEntity>();

    public DbSet<TorrentSeriesEntity> TorrentSeries => Set<TorrentSeriesEntity>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TorrentProjectionSync.ApplyAsync(this, cancellationToken: default, async: false).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await TorrentProjectionSync.ApplyAsync(this, cancellationToken, async: true);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TorrentEntity>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.MamId);
            entity.HasIndex(t => t.NormalizedTitleKey);
            entity.HasIndex(t => t.Language);
            entity.HasIndex(t => t.MediaType);
        });

        modelBuilder.Entity<TorrentPersonEntity>(entity =>
        {
            entity.ToTable("TorrentPeople");
            entity.HasOne<TorrentEntity>().WithMany().HasForeignKey(p => p.TorrentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(p => p.TorrentId);
            entity.HasIndex(p => new { p.Role, p.NameKey });
        });

        modelBuilder.Entity<TorrentSeriesEntity>(entity =>
        {
            entity.ToTable("TorrentSeries");
            entity.HasOne<TorrentEntity>().WithMany().HasForeignKey(s => s.TorrentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(s => s.TorrentId);
            entity.HasIndex(s => s.NameKey);
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
