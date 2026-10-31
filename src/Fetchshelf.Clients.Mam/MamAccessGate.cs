namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Ensures at most one autograbber and one list import talk to MaM at a time.
/// Register as a singleton; the autograbber and list-import features (later roadmap steps) both
/// acquire it around their MaM calls.
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
