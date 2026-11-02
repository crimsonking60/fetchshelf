namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Ensures at most one job talks to MAM at a time. Register as a singleton; every job that calls MAM, such as an
/// autograb or a snatchlist, acquires it around its MAM calls.
/// </summary>
public sealed class MamAccessGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
