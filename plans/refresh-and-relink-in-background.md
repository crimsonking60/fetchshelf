# Refresh and relink in the background

## Problem

**Refresh and relink** on a torrent's page does all its work inside the form post. The handler saves the
**Split into its books** choice, waits for `LibraryWriteGate`, runs `LinkRunner.RefreshAndRelinkAsync` to the end
and only then redirects. The form is a plain post, so the browser shows nothing but its spinner meanwhile.

For a large collection, with hundreds of books, that is bad:

- Nothing shows how far it has got, or that it is still working.
- While the Link or Clean job or a bulk action holds the write gate, the post just waits, before it even starts.
- When the browser or a reverse proxy gives up, the request is aborted. The gate wait stops on that cancellation.

The bulk actions of the Torrents and Library check pages already run in the background through
`BulkOperationQueue` and `BulkOperationWorker`, with a progress panel that polls itself. Refresh and relink is
brought onto the same machinery.

## Design

1. **A new bulk action, `BulkAction.RefreshAndRelink`.** `BulkActionRunner` runs it with
   `LinkRunner.RefreshAndRelinkAsync`, one torrent at a time under the write gate, as for the other actions.
   - Its verbs are "Refreshing and relinking", "Refreshed and relinked".
   - `Describe()` says "Refresh and relink of 1 torrent".
   - When an operation of a single torrent finishes without a problem, its summary is the relink's own message
     (where the book was placed, or "Linked … as N books."), as the page showed before.

2. **Which page shows an operation.** `BulkOperation.OnLibraryCheck` becomes a `BulkOperationPage`:
   `Torrents`, `LibraryCheck` or `Torrent` (a torrent's own page, for `RefreshAndRelink`).
   - `BulkOperationQueue.Shown(page)` replaces `Shown(bool libraryCheck)`.
   - `Shown(BulkOperationPage.Torrent)` also takes the torrent's id and gives the operations of that torrent.
   - A book of a split collection is relinked with its collection, so the operation is queued with the
     collection's hash. The page of one of its books shows it too.
   - The Jobs page links to and cancels through the page the operation belongs to, a torrent's page included.

3. **Per-book progress.** `RelinkAsync` and `RefreshAndRelinkAsync` take an optional callback, called as each
   book of a collection is placed, with how many are placed and how many there are. It is passed down through
   `RelinkCoreAsync` and `RelinkAsBooksAsync` to `LinkBooksAsync`; the Link job passes none.
   - `BulkOperation.ReportBooks(placed, total)` stores it; `Report` for the next torrent clears it.
   - While a torrent's books are being placed, the status adds them: "Refreshing and relinking 1 of 1: Title
     (book 37 of 332)".
   - The progress bar of an operation of one torrent counts the books instead.
   - The bulk Relink of the Torrents page and the Library check's Relink selected pass the callback too.

4. **The torrent page.**
   - The post saves the split choice as before, queues the operation and redirects straight away.
   - It no longer waits for the write gate.
   - If an operation of this torrent is waiting or running already, nothing more is queued, and the page says so.
   - The page shows `_BulkOperationsPartial` for its operations, at the top of the page. It has the
     `BulkOperations`, `CancelBulk` and `DismissBulk` handlers, and polls itself while one is waiting or running.
   - `BulkOperationsViewModel` gets the page's route id, so the panel's URLs and forms keep `{id}`.
   - The panel's link says "Reload the page", not "Reload the list", on a torrent's page.
   - While an operation of this torrent is waiting or running, the **Refresh and relink** button is disabled.
   - **Cancel** is offered while an operation waits, and while a running one has torrents left after the current
     one. An operation of one torrent, once past the wait for the write gate, has nothing left to cancel, so it
     isn't offered then.

5. **Docs.** The user guide says that Refresh and relink runs in the background, with its progress, and a
   collection's progress book by book, at the top of the torrent's page.

## Out of scope

The torrent page's **Refresh**, **Unlink** and **Clean** also run in the post. They handle one book and are quick,
so they stay as they are.

## Tests

- **Web:** the torrent page's Refresh and relink queues an operation and redirects at once. Its panel shows the
  result once the operation has ended: the refusal message, or the relink's own message.
- **Web:** while an operation of the torrent is waiting, a second post queues nothing, and the button is disabled.
- **Web:** a book's page shows its collection's operation.
- **Web:** the existing tests that post `RefreshAndRelink` wait for the operation to end.
- **Unit:** `BulkOperation.Status()` with book progress, and `Shown` per page.
- **Data:** relinking a collection as its books calls the callback once per book, ending at the number of books.
