using System.Threading.Channels;

namespace Fetchshelf.Web.Jobs;

/// <summary>
/// The Torrents page's bulk actions waiting or running, and the last ones that ended, so the page can show their
/// progress and how they went. <see cref="BulkOperationWorker"/> runs them one at a time, in order. A singleton.
/// </summary>
public sealed class BulkOperationQueue(TimeProvider timeProvider)
{
    /// <summary>How many ended operations are kept to be shown; older ones are forgotten.</summary>
    public const int EndedKept = 10;

    private readonly Lock _lock = new();
    private readonly List<BulkOperation> _operations = [];
    private readonly Channel<BulkOperation> _pending = Channel.CreateUnbounded<BulkOperation>(new UnboundedChannelOptions { SingleReader = true });
    private int _lastId;

    /// <summary>What <see cref="BulkOperationWorker"/> takes the operations from, in the order they were queued.</summary>
    public ChannelReader<BulkOperation> Pending => _pending.Reader;

    /// <summary>Queues <paramref name="action"/> on <paramref name="torrentIds"/>, behind any operation waiting or running.</summary>
    /// <param name="origin">For the finds' actions, the page queueing it (<see cref="BulkOperation.Origin"/>).</param>
    public BulkOperation Enqueue(BulkAction action, IReadOnlyList<string> torrentIds, string? category = null, string? origin = null)
    {
        BulkOperation operation;
        lock (_lock)
        {
            operation = new BulkOperation(++_lastId, action, torrentIds, category, timeProvider.GetUtcNow(), origin);
            _operations.Add(operation);
        }

        _pending.Writer.TryWrite(operation);
        return operation;
    }

    /// <summary>The operations the Torrents page shows: running, waiting, and ended but not dismissed, oldest first.</summary>
    public IReadOnlyList<BulkOperation> Shown()
    {
        lock (_lock)
        {
            return _operations.Where(o => !o.Dismissed).ToList();
        }
    }

    /// <summary>Those <see cref="Shown()"/> on one page: the Torrents page or the Library check (a torrent's page uses <see cref="ShownForTorrent"/>).</summary>
    public IReadOnlyList<BulkOperation> Shown(BulkOperationPage page) => Shown().Where(o => o.Page == page).ToList();

    /// <summary>
    /// Those <see cref="Shown()"/> on the page of the torrent <paramref name="torrentId"/>: its own, and, for a book of a
    /// split collection, its collection's, which relinks the book with the rest.
    /// </summary>
    public IReadOnlyList<BulkOperation> ShownForTorrent(string torrentId)
    {
        var collectionId = Core.Organizer.CollectionBookId.HashOf(torrentId);
        return Shown().Where(o => o.Page == BulkOperationPage.Torrent && o.ItemIds.Any(id => id == torrentId || id == collectionId)).ToList();
    }

    /// <summary>Those <see cref="Shown()"/> on a follow's page (its id as <paramref name="origin"/>) or the To look at page (null).</summary>
    public IReadOnlyList<BulkOperation> ShownForFinds(string? origin) =>
        Shown().Where(o => o.Page == BulkOperationPage.Finds && o.Origin == origin).ToList();

    /// <summary>The operations running or waiting, oldest first.</summary>
    public IReadOnlyList<BulkOperation> Active()
    {
        lock (_lock)
        {
            return _operations.Where(o => !o.HasEnded).ToList();
        }
    }

    /// <summary>
    /// Cancels the operation: one waiting never starts, one running stops after the torrent it is on. False when there
    /// is no such operation, or it has ended.
    /// </summary>
    public bool Cancel(int id)
    {
        var operation = Find(id);
        if (operation is null || operation.HasEnded)
        {
            return false;
        }

        if (operation.TryCancelWhileWaiting())
        {
            Ended(operation);
        }
        else
        {
            operation.Cancellation.Cancel();
        }

        return true;
    }

    /// <summary>Hides an ended operation from the page. False when there is no such operation, or it hasn't ended.</summary>
    public bool Dismiss(int id)
    {
        var operation = Find(id);
        if (operation is null || !operation.HasEnded)
        {
            return false;
        }

        operation.Dismiss();
        return true;
    }

    /// <summary>Forgets the oldest ended operations beyond <see cref="EndedKept"/>; called as each one ends.</summary>
    public void Ended(BulkOperation operation)
    {
        lock (_lock)
        {
            var ended = _operations.Where(o => o.HasEnded).ToList();
            foreach (var old in ended.Take(Math.Max(0, ended.Count - EndedKept)))
            {
                _operations.Remove(old);
            }
        }
    }

    private BulkOperation? Find(int id)
    {
        lock (_lock)
        {
            return _operations.FirstOrDefault(o => o.Id == id);
        }
    }
}
