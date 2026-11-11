namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Ensures at most one job talks to MAM at a time. Register as a singleton; every job that calls MAM, such as an
/// autograb or a snatchlist, acquires it around its MAM calls, naming itself so a page can say who holds it.
/// </summary>
public sealed class MamAccessGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private volatile string? _owner;

    /// <summary>Who holds the gate, such as "Autograb Freeleech"; null while it is free.</summary>
    public string? Owner => _owner;

    /// <param name="owner">Who is acquiring the gate, as a user would recognise it.</param>
    /// <param name="cancellationToken">Stops waiting for the gate.</param>
    public async Task<IDisposable> AcquireAsync(string owner, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        _owner = owner;
        return new Releaser(this);
    }

    /// <summary>Acquires the gate if it comes free within <paramref name="timeout"/>; null if it doesn't.</summary>
    public async Task<IDisposable?> TryAcquireAsync(string owner, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!await _semaphore.WaitAsync(timeout, cancellationToken))
        {
            return null;
        }

        _owner = owner;
        return new Releaser(this);
    }

    private sealed class Releaser(MamAccessGate gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate._owner = null;
                gate._semaphore.Release();
            }
        }
    }
}
