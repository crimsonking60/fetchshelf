namespace Fetchshelf.Clients.Covers;

/// <summary>Keeps at least a given time between the start of one request and the next, however many callers there are.</summary>
public sealed class RequestSpacer(TimeSpan spacing, TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
    private DateTimeOffset? _last;

    /// <summary>Waits until the next request may start, and counts it as started.</summary>
    public async Task WaitTurnAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_last is { } last && last + spacing - _time.GetUtcNow() is var wait && wait > TimeSpan.Zero)
            {
                await _delay(wait, cancellationToken);
            }

            _last = _time.GetUtcNow();
        }
        finally
        {
            _lock.Release();
        }
    }
}
