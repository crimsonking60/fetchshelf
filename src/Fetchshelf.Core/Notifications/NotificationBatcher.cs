namespace Fetchshelf.Core.Notifications;

/// <summary>
/// Holds back bursts of <see cref="NotificationEvent.Grabbed"/> and <see cref="NotificationEvent.Linked"/> and merges
/// each into one message: a Hardcover list run selecting 20 books sends one message, not 20. Every other event passes
/// straight through. It has no clock or timer: the caller says what time it is, and asks what is ready.
/// </summary>
public sealed class NotificationBatcher
{
    /// <summary>How long after its last arrival a burst is sent: grabs and links within this of each other become one message.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(30);

    /// <summary>The longest a burst is held, so one that never pauses is still sent.</summary>
    public static readonly TimeSpan MaxHold = TimeSpan.FromMinutes(5);

    /// <summary>The most names a merged message lists before it says "and 2 more".</summary>
    public const int NamesShown = 3;

    private sealed class Burst(DateTimeOffset firstAt)
    {
        public DateTimeOffset FirstAt { get; } = firstAt;

        public DateTimeOffset LastAt { get; set; } = firstAt;

        public List<Notification> Items { get; } = [];
    }

    private readonly Dictionary<NotificationEvent, Burst> _bursts = [];
    private readonly List<Notification> _passed = [];

    /// <summary>Whether this event is merged when several arrive close together.</summary>
    public static bool IsBatched(NotificationEvent notificationEvent) => notificationEvent is NotificationEvent.Grabbed or NotificationEvent.Linked;

    /// <summary>Takes a notification that arrived at <paramref name="now"/>.</summary>
    public void Add(Notification notification, DateTimeOffset now)
    {
        if (!IsBatched(notification.Event))
        {
            _passed.Add(notification);
            return;
        }

        if (!_bursts.TryGetValue(notification.Event, out var burst))
        {
            burst = _bursts[notification.Event] = new Burst(now);
        }

        burst.LastAt = now;
        burst.Items.Add(notification);
    }

    /// <summary>When the next burst is due, so the caller knows how long to wait; null when none is held.</summary>
    public DateTimeOffset? NextDue => _bursts.Count == 0 ? null : _bursts.Values.Min(Due);

    /// <summary>Whether anything is waiting to be sent, now or later.</summary>
    public bool HasPending => _passed.Count > 0 || _bursts.Count > 0;

    /// <summary>
    /// What is ready at <paramref name="now"/>, oldest first: the notifications that pass through, and the bursts that
    /// have gone quiet for <see cref="Gap"/> or been held for <see cref="MaxHold"/>. With <paramref name="everything"/>, every
    /// held burst too, as when Fetchshelf is stopping.
    /// </summary>
    public IReadOnlyList<Notification> TakeReady(DateTimeOffset now, bool everything = false)
    {
        var ready = new List<Notification>(_passed);
        _passed.Clear();
        foreach (var (notificationEvent, burst) in _bursts.OrderBy(pair => pair.Value.FirstAt).ToList())
        {
            if (everything || Due(burst) <= now)
            {
                ready.Add(Merge(burst.Items));
                _bursts.Remove(notificationEvent);
            }
        }

        return ready;
    }

    private static DateTimeOffset Due(Burst burst)
    {
        var quiet = burst.LastAt + Gap;
        var held = burst.FirstAt + MaxHold;
        return quiet < held ? quiet : held;
    }

    /// <summary>One notification for a burst: the notification itself when it is alone, else a message that names the first few.</summary>
    public static Notification Merge(IReadOnlyList<Notification> burst)
    {
        var first = burst[0];
        if (burst.Count == 1)
        {
            return first;
        }

        var verb = first.Event == NotificationEvent.Grabbed ? "Grabbed" : "Linked";
        var names = burst.Select(n => n.Item ?? n.Title).ToList();
        var heading = $"{verb} {burst.Count} torrents";

        // Every item keeps its own fields (torrent and MAM ids among them), so a webhook can act on each.
        var items = burst.Select(n => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(n.Fields ?? new Dictionary<string, object?>())
        {
            ["name"] = n.Item ?? n.Title,
            ["time"] = n.Time,
        }).ToList();
        return new Notification(
            first.Event,
            heading,
            $"{heading}: {NameList(names)}.",
            first.Time,
            new Dictionary<string, object?> { ["count"] = burst.Count, ["names"] = names, ["items"] = items },
            // No one torrent's page: the Events page lists them all.
            $"/Events?Type={first.Event}");
    }

    /// <summary>"A and B", "A, B and C" or, past <see cref="NamesShown"/>, "A, B, C and 2 more".</summary>
    public static string NameList(IReadOnlyList<string> names)
    {
        if (names.Count <= NamesShown)
        {
            return names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
        }

        return $"{string.Join(", ", names.Take(NamesShown))} and {names.Count - NamesShown} more";
    }
}
