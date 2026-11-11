using Fetchshelf.Core.Notifications;

namespace Fetchshelf.Core.Tests;

public class NotificationBatcherTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static Notification Grabbed(string item) => new(
        NotificationEvent.Grabbed, $"Grabbed {item}", $"Grabbed {item}.", T0, new Dictionary<string, object?> { ["x"] = 1 }, "/torrents/x", Item: item);

    private static Notification Linked(string item) =>
        new(NotificationEvent.Linked, $"Linked {item}", $"Linked {item} into Audiobooks.", T0, LinkPath: "/torrents/y", Item: item);

    private static Notification Failing() => new(NotificationEvent.JobFailing, "Job Link is failing", "Job Link failed.", T0);

    [Fact]
    public void FourGrabsWithin30SecondsOfEachOther_AreOneMessageNamingThree()
    {
        var batcher = new NotificationBatcher();
        foreach (var (item, seconds) in new[] { ("A", 0), ("B", 10), ("C", 25), ("D", 50) })
        {
            batcher.Add(Grabbed(item), T0.AddSeconds(seconds)); // each within 30 s of the one before
        }

        Assert.Empty(batcher.TakeReady(T0.AddSeconds(79))); // 29 s after the last one
        var message = Assert.Single(batcher.TakeReady(T0.AddSeconds(80)));

        Assert.Equal(("Grabbed 4 torrents", "Grabbed 4 torrents: A, B, C and 1 more."), (message.Title, message.Message));
        Assert.Equal(4, message.Fields!["count"]);
        Assert.Equal(["A", "B", "C", "D"], (IEnumerable<string>)message.Fields["names"]!);
        // Each item keeps its own fields, so a webhook can act on every torrent, and the link goes to all of them.
        var items = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)message.Fields["items"]!;
        Assert.Equal(["A", "B", "C", "D"], items.Select(i => (string)i["name"]!));
        Assert.All(items, i => Assert.Equal(1, i["x"]));
        Assert.Equal("/Events?Type=Grabbed", message.LinkPath);
        Assert.False(batcher.HasPending);
    }

    [Fact]
    public void AGrabFortySecondsLater_IsItsOwnMessage()
    {
        var batcher = new NotificationBatcher();
        batcher.Add(Grabbed("A"), T0);
        batcher.Add(Grabbed("B"), T0.AddSeconds(5));
        var first = Assert.Single(batcher.TakeReady(T0.AddSeconds(40)));
        batcher.Add(Grabbed("C"), T0.AddSeconds(45));
        var second = Assert.Single(batcher.TakeReady(T0.AddSeconds(76)));

        Assert.Equal("Grabbed 2 torrents: A and B.", first.Message);
        Assert.Equal("Grabbed C.", second.Message); // alone, so as it was: its own text, link and fields
        Assert.Equal("/torrents/x", second.LinkPath);
    }

    [Fact]
    public void GrabsAndLinks_AreMergedSeparately()
    {
        var batcher = new NotificationBatcher();
        batcher.Add(Grabbed("A"), T0);
        batcher.Add(Linked("X"), T0.AddSeconds(1));
        batcher.Add(Grabbed("B"), T0.AddSeconds(2));
        batcher.Add(Linked("Y"), T0.AddSeconds(3));

        var ready = batcher.TakeReady(T0.AddMinutes(1));

        Assert.Equal(["Grabbed 2 torrents: A and B.", "Linked 2 torrents: X and Y."], ready.Select(n => n.Message));
    }

    [Fact]
    public void OtherEvents_PassStraightThroughInOrder()
    {
        var batcher = new NotificationBatcher();
        batcher.Add(Failing(), T0);
        batcher.Add(Grabbed("A"), T0);

        var ready = batcher.TakeReady(T0);

        Assert.Equal(NotificationEvent.JobFailing, Assert.Single(ready).Event);
        Assert.True(batcher.HasPending); // the grab is still being held
    }

    [Fact]
    public void ABurstThatNeverPauses_IsSentAfterTheMaximumHold()
    {
        var batcher = new NotificationBatcher();
        for (var seconds = 0; seconds <= 330; seconds += 20)
        {
            batcher.Add(Grabbed($"T{seconds}"), T0.AddSeconds(seconds));
        }

        Assert.NotNull(batcher.NextDue);
        Assert.Equal(T0 + NotificationBatcher.MaxHold, batcher.NextDue);
        Assert.Single(batcher.TakeReady(T0 + NotificationBatcher.MaxHold));
    }

    [Fact]
    public void NextDue_IsTheEndOfTheQuietGap_AndNothingWhenNothingIsHeld()
    {
        var batcher = new NotificationBatcher();
        Assert.Null(batcher.NextDue);

        batcher.Add(Grabbed("A"), T0);
        batcher.Add(Grabbed("B"), T0.AddSeconds(10));

        Assert.Equal(T0.AddSeconds(40), batcher.NextDue);
    }

    [Fact]
    public void TakeReady_Everything_SendsHeldBurstsAtOnce_AsWhenStopping()
    {
        var batcher = new NotificationBatcher();
        batcher.Add(Grabbed("A"), T0);

        Assert.Single(batcher.TakeReady(T0, everything: true));
        Assert.False(batcher.HasPending);
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(2, "A and B")]
    [InlineData(3, "A, B and C")]
    [InlineData(4, "A, B, C and 1 more")]
    [InlineData(6, "A, B, C and 3 more")]
    public void NameList_NamesUpToThreeAndCountsTheRest(int count, string expected) =>
        Assert.Equal(expected, NotificationBatcher.NameList(new[] { "A", "B", "C", "D", "E", "F" }[..count]));
}
