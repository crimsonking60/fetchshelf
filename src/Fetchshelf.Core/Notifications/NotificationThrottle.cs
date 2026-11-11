namespace Fetchshelf.Core.Notifications;

/// <summary>Lets something through at most once in a while: a rate limit reported every minute is reported once an hour.</summary>
public sealed class NotificationThrottle
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _last = [];

    /// <summary>
    /// Whether <paramref name="key"/> may be sent at <paramref name="now"/>: it hasn't been within
    /// <paramref name="interval"/>. Saying yes counts as sending it.
    /// </summary>
    public bool Allow(string key, TimeSpan interval, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_last.TryGetValue(key, out var last) && now - last < interval)
            {
                return false;
            }

            _last[key] = now;
            return true;
        }
    }
}
