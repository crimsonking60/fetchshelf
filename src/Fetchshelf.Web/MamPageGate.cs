using Fetchshelf.Clients.Mam;

namespace Fetchshelf.Web;

/// <summary>
/// The MAM gate as a page action takes it: waiting <see cref="Wait"/> at most for a job that holds it, then saying who is
/// busy rather than queueing behind a run that can take minutes. Every page action that calls <see cref="MamClient"/>
/// goes through it, so it never talks to MAM alongside a job, and never selects alongside a job's selection.
/// </summary>
/// <param name="wait">How long to wait for the gate; <see cref="MamAccessGate.PageWait"/> unless a test sets a shorter one.</param>
public sealed class MamPageGate(MamAccessGate gate, TimeSpan? wait = null)
{
    public TimeSpan Wait { get; } = wait ?? MamAccessGate.PageWait;

    /// <summary>The gate, held until disposed; null when a job still holds it after <see cref="Wait"/>.</summary>
    /// <param name="owner">Who is taking it, as the Account page shows it, such as "Search page".</param>
    public Task<IDisposable?> TryEnterAsync(string owner, CancellationToken cancellationToken) =>
        gate.TryAcquireAsync(owner, Wait, cancellationToken);

    /// <summary>What to tell the user when <see cref="TryEnterAsync"/> gave up, naming who holds the gate.</summary>
    public string BusyMessage => $"MAM is busy with {gate.Owner ?? "another job"}; try again in a moment.";
}
