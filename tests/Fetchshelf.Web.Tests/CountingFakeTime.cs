using Microsoft.Extensions.Time.Testing;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// A fake clock that counts the timers created on it, so a test moves the clock only once the code under test waits on
/// it: moved earlier, a delay would be measured from the new time and never end.
/// </summary>
internal sealed class CountingFakeTime(DateTimeOffset start) : FakeTimeProvider(start)
{
    private int _timers;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timers);
        return timer;
    }

    public async Task WaitForTimersAsync(int count)
    {
        for (var i = 0; i < 500 && Volatile.Read(ref _timers) < count; i++)
        {
            await Task.Delay(10);
        }
    }
}
