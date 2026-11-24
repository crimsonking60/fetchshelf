namespace Fetchshelf.Web.Jobs;

/// <summary>
/// What a bulk action does to each item selected: the torrents of the Torrents page, or, for
/// <see cref="DeleteFindings"/> and <see cref="RelinkFindings"/>, the findings of the Library check page. A torrent's
/// own page queues <see cref="RefreshAndRelink"/>.
/// </summary>
public enum BulkAction
{
    Relink,
    Unlink,
    Refresh,
    Clean,
    SetCategory,

    /// <summary>The Library check's Delete selected: unknown folders and files deleted, missing books' records forgotten.</summary>
    DeleteFindings,

    /// <summary>The Library check's Relink selected, for books whose folder or files are gone.</summary>
    RelinkFindings,

    /// <summary><b>Refresh and relink</b> on a torrent's own page.</summary>
    RefreshAndRelink,
}

/// <summary>The page that queued a bulk operation, which shows its progress and how it went.</summary>
public enum BulkOperationPage
{
    Torrents,
    LibraryCheck,

    /// <summary>A torrent's own page, for the torrent the operation is about.</summary>
    Torrent,
}

/// <summary>Where a bulk operation is: waiting behind another, running, or ended one of three ways.</summary>
public enum BulkOperationState
{
    Waiting,
    Running,
    Finished,
    Cancelled,
    Interrupted,
}

/// <summary>
/// One bulk action on the torrents selected on the Torrents page, or the findings selected on the Library check page, run
/// in the background by <see cref="BulkOperationWorker"/>, one operation at a time. Kept in memory only: a restart forgets
/// it, and each item's own result is recorded where it always is (events, the Errors page, the Library check's findings).
/// </summary>
public sealed class BulkOperation
{
    private readonly Lock _lock = new();
    private int _handled;
    private string? _current;
    private (int Placed, int Total)? _books;
    private bool _handling;

    public BulkOperation(int id, BulkAction action, IReadOnlyList<string> itemIds, string? category, DateTimeOffset createdAt)
    {
        Id = id;
        Action = action;
        ItemIds = itemIds;
        Category = category;
        CreatedAt = createdAt;
    }

    public int Id { get; }

    public BulkAction Action { get; }

    public IReadOnlyList<string> ItemIds { get; }

    /// <summary>The category <see cref="BulkAction.SetCategory"/> sets; null for the other actions.</summary>
    public string? Category { get; }

    public DateTimeOffset CreatedAt { get; }

    public BulkOperationState State { get; private set; } = BulkOperationState.Waiting;

    /// <summary>What it did, once it has ended: the summary the page shows.</summary>
    public string? Summary { get; private set; }

    /// <summary>Each torrent that was refused or failed, with why: "Title: why".</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    /// <summary>Whether some torrent was refused or failed, so the summary is shown as a problem.</summary>
    public bool HadProblems => Problems.Count > 0;

    /// <summary>Hidden by <b>Dismiss</b>; an ended operation shows until then.</summary>
    public bool Dismissed { get; private set; }

    /// <summary>Cancelled by <b>Cancel</b>: the operation stops after the torrent it is on.</summary>
    public CancellationTokenSource Cancellation { get; } = new();

    /// <summary>
    /// Whether <b>Cancel</b> is offered: while it waits, or while torrents are left after the one being handled. An
    /// operation of one torrent being handled is handled completely, so there is nothing left to cancel.
    /// </summary>
    public bool CanCancel
    {
        get
        {
            lock (_lock)
            {
                return State == BulkOperationState.Waiting || (State == BulkOperationState.Running && (ItemIds.Count > 1 || !_handling));
            }
        }
    }

    public bool HasEnded => State is BulkOperationState.Finished or BulkOperationState.Cancelled or BulkOperationState.Interrupted;

    /// <summary>For the torrent being handled, when it is a collection linked as its books: how many are placed, of how many.</summary>
    public (int Placed, int Total)? Books
    {
        get
        {
            lock (_lock)
            {
                return _books;
            }
        }
    }

    /// <summary>How many torrents have been handled so far, and the title of the one being handled now.</summary>
    public (int Handled, string? Current) Progress
    {
        get
        {
            lock (_lock)
            {
                return (_handled, _current);
            }
        }
    }

    /// <summary>Marks it running, unless it was cancelled while it waited. False then: it is not to run.</summary>
    public bool TryStart()
    {
        lock (_lock)
        {
            if (State != BulkOperationState.Waiting)
            {
                return false;
            }

            State = BulkOperationState.Running;
            return true;
        }
    }

    /// <summary>Ends it as cancelled if it is still waiting. False when it has started or ended already.</summary>
    public bool TryCancelWhileWaiting()
    {
        lock (_lock)
        {
            if (State != BulkOperationState.Waiting)
            {
                return false;
            }

            (State, Summary) = (BulkOperationState.Cancelled, $"{Describe()}: cancelled before it started.");
            return true;
        }
    }

    public void Report(int handled, string? current)
    {
        lock (_lock)
        {
            (_handled, _current, _books, _handling) = (handled, current, null, false);
        }
    }

    /// <summary>The torrent <see cref="Report"/> named is being handled now, past the wait for the write gate.</summary>
    public void Handling()
    {
        lock (_lock)
        {
            _handling = true;
        }
    }

    /// <summary>How many books of the torrent being handled are placed, of how many; cleared by the next <see cref="Report"/>.</summary>
    public void ReportBooks(int placed, int total)
    {
        lock (_lock)
        {
            _books = (placed, total);
        }
    }

    /// <summary>
    /// What the progress bar shows, as a value of a maximum: the torrents handled, or, for an operation of one torrent, the
    /// books of its collection placed once they are being placed.
    /// </summary>
    public (int Value, int Max) ProgressBar()
    {
        lock (_lock)
        {
            return ItemIds.Count == 1 && _books is { } books ? (books.Placed, books.Total) : (_handled, ItemIds.Count);
        }
    }

    public void End(BulkOperationState state, string summary, IReadOnlyList<string>? problems = null)
    {
        lock (_lock)
        {
            (Summary, Problems, _current, _books, State) = (summary, problems ?? [], null, null, state);
        }
    }

    public void Dismiss() => Dismissed = true;

    /// <summary>The action as a noun, with what it is about: "Relink of 23 torrents".</summary>
    public string Describe() => Action switch
    {
        BulkAction.SetCategory => $"Setting the category to {Category} for {Count(ItemIds.Count)}",
        BulkAction.DeleteFindings or BulkAction.RelinkFindings => $"{Verbs.For(Action, Category).Doing} {Count(ItemIds.Count)}",
        BulkAction.RefreshAndRelink => $"Refresh and relink of {Count(ItemIds.Count)}",
        _ => $"{Action} of {Count(ItemIds.Count)}",
    };

    /// <summary>The page that shows it: the Library check for its findings, a torrent's page for its Refresh and relink, else the Torrents page.</summary>
    public BulkOperationPage Page => Action switch
    {
        BulkAction.DeleteFindings or BulkAction.RelinkFindings => BulkOperationPage.LibraryCheck,
        BulkAction.RefreshAndRelink => BulkOperationPage.Torrent,
        _ => BulkOperationPage.Torrents,
    };

    /// <summary>A number of the items it acts on: "1 torrent", "23 findings", "4 books".</summary>
    public string Count(int count)
    {
        var (one, many) = Action switch
        {
            BulkAction.DeleteFindings => ("finding", "findings"),
            BulkAction.RelinkFindings => ("book", "books"),
            _ => ("torrent", "torrents"),
        };
        return count == 1 ? $"1 {one}" : $"{count} {many}";
    }

    public string Status()
    {
        var (handled, current) = Progress;
        var books = Books;
        return State switch
        {
            BulkOperationState.Waiting => $"{Describe()}: waits for the bulk action before it.",
            BulkOperationState.Running => $"{Verbs.For(Action, Category).Doing} {Math.Min(handled + 1, ItemIds.Count)} of {ItemIds.Count}"
                + (current is null ? "" : $": {current}")
                + (books is { } b ? $" (book {b.Placed} of {b.Total})" : ""),
            _ => Summary ?? string.Empty,
        };
    }

    /// <summary>What each action says while it runs and when it is done, and when one torrent fails.</summary>
    public sealed record Verbs(string Doing, string Done, string Failing)
    {
        public static Verbs For(BulkAction action, string? category) => action switch
        {
            BulkAction.Relink => new("Relinking", "Relinked", "Relinking"),
            BulkAction.Unlink => new("Unlinking", "Unlinked", "Unlinking"),
            BulkAction.Refresh => new("Refreshing", "Refreshed", "Refreshing"),
            BulkAction.Clean => new("Cleaning", "Cleaned", "Cleaning"),
            BulkAction.DeleteFindings => new("Deleting", "Deleted", "Deleting"),
            BulkAction.RelinkFindings => new("Relinking", "Relinked", "Relinking"),
            BulkAction.RefreshAndRelink => new("Refreshing and relinking", "Refreshed and relinked", "Refresh and relink"),
            _ => new($"Setting the category to {category} for", $"Set the category to {category} for", "Setting the category"),
        };
    }
}
