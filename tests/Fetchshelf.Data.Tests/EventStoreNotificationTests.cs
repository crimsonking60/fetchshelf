using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fetchshelf.Data.Tests;

/// <summary>What <see cref="EventStore"/> tells the notification sink: once an event is saved, and only then.</summary>
public sealed class EventStoreNotificationTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-event-notify-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private sealed class RecordingSink(bool wanted = true) : INotificationSink
    {
        public List<Notification> Published { get; } = [];

        public List<NotificationEvent> Asked { get; } = [];

        public void Publish(Notification notification) => Published.Add(notification);

        public bool Wants(NotificationEvent notificationEvent)
        {
            Asked.Add(notificationEvent);
            return wanted;
        }
    }

    private sealed class ThrowingSink : INotificationSink
    {
        public void Publish(Notification notification) => throw new InvalidOperationException("sink broke");

        public bool Wants(NotificationEvent notificationEvent) => true;
    }

    private sealed class FailingSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("disk full");
    }

    private async Task<(FetchshelfDbContext Context, RecordingSink Sink, EventStore Store)> CreateAsync()
    {
        var context = OrganizerTestSupport.CreateContext(_dbPath);
        await OrganizerTestSupport.SeedTorrentAsync(context, "hash1", mamId: 7); // "Book 7" by Brandon Sanderson
        var sink = new RecordingSink();
        return (context, sink, new EventStore(context, sink));
    }

    [Fact]
    public async Task AppendAsync_GrabbedEvent_IsPublishedWithTheTorrentsTitleAuthorsHowAndALink()
    {
        var (context, sink, store) = await CreateAsync();
        using var _ = context;

        await store.AppendAsync(new EventEntity
        {
            OccurredAt = At,
            TorrentId = "hash1",
            MamId = 7,
            Kind = EventKind.Grabbed,
            GrabberName = "Autograb Freeleech",
            Cost = "GlobalFreeleech",
            WedgeUsed = false,
        });

        var notification = Assert.Single(sink.Published);
        Assert.Equal(NotificationEvent.Grabbed, notification.Event);
        Assert.Equal("Grabbed Book 7 by Brandon Sanderson (Autograb Freeleech, freeleech).", notification.Message);
        Assert.Equal(("/torrents/hash1", "Book 7 by Brandon Sanderson", At), (notification.LinkPath, notification.Item, notification.Time));
        Assert.Equal(7L, notification.Fields!["mam_id"]);
        Assert.Equal("Autograb Freeleech", notification.Fields["grabber"]);
    }

    [Fact]
    public async Task AppendAsync_LinkedEvent_NamesTheLibrary()
    {
        var (context, sink, store) = await CreateAsync();
        using var _ = context;

        await store.AppendAsync(new EventEntity
        {
            OccurredAt = At,
            TorrentId = "hash1",
            MamId = 7,
            Kind = EventKind.Linked,
            LinkerName = "Audiobooks",
            LibraryPath = "/lib/Book",
        });

        Assert.Equal("Linked Book 7 by Brandon Sanderson into Audiobooks.", Assert.Single(sink.Published).Message);
    }

    [Fact]
    public async Task AppendAsync_RemovedFromMamEvent_SaysSo()
    {
        var (context, sink, store) = await CreateAsync();
        using var _ = context;

        await store.AppendAsync(new EventEntity { OccurredAt = At, TorrentId = "hash1", MamId = 7, Kind = EventKind.RemovedFromMam });

        var notification = Assert.Single(sink.Published);
        Assert.Equal((NotificationEvent.RemovedFromMam, "MAM no longer has Book 7 by Brandon Sanderson."), (notification.Event, notification.Message));
    }

    [Theory]
    [InlineData(EventKind.Updated)]
    [InlineData(EventKind.Cleaned)]
    [InlineData(EventKind.Unlinked)]
    public async Task AppendAsync_OtherKinds_PublishNothing(EventKind kind)
    {
        var (context, sink, store) = await CreateAsync();
        using var _ = context;

        await store.AppendAsync(new EventEntity { OccurredAt = At, TorrentId = "hash1", MamId = 7, Kind = kind });

        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task AppendAsync_EventThatCannotBeSaved_PublishesNothing()
    {
        await using (var setup = OrganizerTestSupport.CreateContext(_dbPath))
        {
            await OrganizerTestSupport.SeedTorrentAsync(setup, "hash1", mamId: 7);
        }

        var failing = new FailingSave();
        using var context = new FetchshelfDbContext(
            new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={_dbPath}").AddInterceptors(failing).Options);
        var sink = new RecordingSink();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EventStore(context, sink).AppendAsync(new EventEntity { OccurredAt = At, TorrentId = "hash1", MamId = 7, Kind = EventKind.Grabbed }));

        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task AppendAsync_SinkThatThrows_DoesNotFailTheAppend()
    {
        using var context = OrganizerTestSupport.CreateContext(_dbPath);
        await OrganizerTestSupport.SeedTorrentAsync(context, "hash1", mamId: 7);

        await new EventStore(context, new ThrowingSink()).AppendAsync(
            new EventEntity { OccurredAt = At, TorrentId = "hash1", MamId = 7, Kind = EventKind.Grabbed });

        Assert.Single(context.Events); // the event is saved all the same
    }

    [Fact]
    public async Task AppendAsync_EventOfATorrentNotYetSaved_StillUsesItsTitleFromTheSameContext()
    {
        using var context = OrganizerTestSupport.CreateContext(_dbPath);
        var sink = new RecordingSink();
        var metadata = OrganizerTestSupport.BuildMetadata("Fresh Book");
        context.Torrents.Add(new TorrentEntity
        {
            Id = "hash9",
            MamId = 9,
            NormalizedTitleKey = "fresh book",
            Title = "Fresh Book",
            MetadataJson = Core.Organizer.TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = At,
        });

        // As the Download job does: the torrent is added to the context, and saved along with the event.
        await new EventStore(context, sink).AppendAsync(new EventEntity { OccurredAt = At, TorrentId = "hash9", MamId = 9, Kind = EventKind.Grabbed });

        Assert.Equal("Grabbed Fresh Book by Brandon Sanderson.", Assert.Single(sink.Published).Message);
    }

    [Fact]
    public async Task AppendAsync_NobodyWantsTheEvent_SkipsReadingTheTorrent()
    {
        await using (var setup = OrganizerTestSupport.CreateContext(_dbPath))
        {
            await OrganizerTestSupport.SeedTorrentAsync(setup, "hash1", mamId: 7);
        }

        // A context that counts its queries: the event's save, and nothing more.
        var queries = new QueryCounter();
        using var context = new FetchshelfDbContext(
            new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={_dbPath}").AddInterceptors(queries).Options);
        var sink = new RecordingSink(wanted: false);

        await new EventStore(context, sink).AppendAsync(new EventEntity { OccurredAt = At, TorrentId = "hash1", MamId = 7, Kind = EventKind.Linked });

        Assert.Equal([NotificationEvent.Linked], sink.Asked);
        Assert.Empty(sink.Published);
        Assert.DoesNotContain(queries.Commands, c => c.Contains("FROM \"Torrents\"", StringComparison.Ordinal));
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task AppendAsync_WithoutASink_JustSavesTheEvent()
    {
        using var context = OrganizerTestSupport.CreateContext(_dbPath);

        await new EventStore(context).AppendAsync(new EventEntity { OccurredAt = At, Kind = EventKind.Grabbed });

        Assert.Single(context.Events);
    }

    [Theory]
    [InlineData("GlobalFreeleech", null, "freeleech")]
    [InlineData("PersonalFreeleech", null, "personal freeleech")]
    [InlineData("Vip", null, "VIP")]
    [InlineData("Ratio", false, "ratio")]
    [InlineData("UseWedge", true, "wedge used")]
    [InlineData("TryWedge", false, "no wedge needed")]
    [InlineData(null, null, null)]
    public void DescribeCost_ReadsAsAPersonWouldSayIt(string? cost, bool? wedgeUsed, string? expected) =>
        Assert.Equal(expected, EventNotifications.DescribeCost(cost, wedgeUsed));
}
