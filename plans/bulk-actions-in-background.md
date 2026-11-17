# Bulk actions on the Torrents page in the background — implementation plan

## Context

Selecting torrents on the **Torrents** page and choosing **Relink**, **Unlink**, **Refresh**, **Clean** or
**Set category** posts the form to `OnPostBulkAsync` (`Pages/Torrents/Index.cshtml.cs`). That request works through
the torrents one at a time and then redirects to the list with a summary in `TempData`. With twenty or more
torrents:

- Nothing on the page shows that work is going on until the request ends; only the browser's own loading
  indicator does.
- Relink, Unlink and Set category pass `HttpContext.RequestAborted` to each step. Leaving the page cancels the
  request, so the loop stops after whichever torrent it was on, and the rest are left as they were.
- Refresh and Clean don't take that token and run on after the page is left, but their summary goes to a redirect
  nobody receives, so it is lost.

Each torrent's own result is already recorded where it belongs (Linked and Unlinked events, errors on the
**Errors** page). Only the run as a whole has no home.

Nothing stops a bulk relink or unlink from working on a torrent at the same moment as the Link or Clean job. That
risk exists today; running bulk actions in the background makes it likelier, so this plan closes it.

## Change

### 1. A queue for bulk operations (Web)

`BulkOperationQueue`, a singleton, with a hosted `BulkOperationWorker`:

- `Enqueue(BulkAction action, IReadOnlyList<string> torrentIds, string? category)` returns a `BulkOperation` with an
  id. Operations run one at a time, in order; one queued behind another waits.
- The worker runs each in a scope of its own (`IServiceScopeFactory`), not tied to any request, with a token that
  only **Cancel** and shutdown cancel.
- `BulkOperation` holds, in memory: the action, how many there are, how many are done, the title being worked on,
  the problems so far, when it started and ended, and how it ended (finished, cancelled, interrupted by shutdown).
  The last 10 ended operations are kept so a summary can still be read later; a restart forgets them, which is fine
  since each torrent's result is recorded anyway.

### 2. The work itself moves out of the page

The per-torrent loop, `RunEachAsync`, and the five actions move from the page model into `BulkActionRunner`
(Web, as built: the actions need the Web project's `QBittorrentConnections` and `MamErrorDescriber`), unchanged in
what they do:

- one torrent at a time, a failure reported and the rest still run, `DiscardPendingChanges` after a failure;
- the summary text as today ("Relinked 18 of 23 torrents. Title: reason …");
- Refresh's MAM lookups take the MAM gate per call, as they do now.

`OnPostBulkAsync` then only checks the input (a category chosen for **Set category**, as now), enqueues, and
redirects to the list. **Remove record** stays in the request: it is one database delete.

### 3. No overlap with the Link and Clean jobs

`LibraryWriteGate`, a singleton semaphore (Data), taken per torrent: by `BulkActionRunner` around each torrent, by
`LinkRunner.RunAsync` around placing one torrent's files, and by `Cleaner` around removing one torrent's files. Per
torrent, not per run, so a long Link run and a bulk operation take turns instead of one waiting for the other to
finish. The torrent page's single **Relink** and **Unlink** take it too. The callers take it, never
`RelinkAsync` or `UnlinkAsync` themselves: the semaphore isn't reentrant, and the Link job's run calls into the same
code.

### 4. Progress on the Torrents page

A panel above the list, `_BulkOperationPartial.cshtml`, polled with htmx every 2 seconds while an operation runs or
waits, as the Jobs page polls its list:

- running: "Relinking 7 of 23: *title*", a progress bar, and **Cancel**, which stops after the current torrent;
- waiting: "Unlink of 5 torrents waits for the relink above";
- ended: the summary, as the message above the list shows it today, with **Dismiss**. It stays until dismissed,
  so coming back to the page later still shows how it went.

While an operation runs, the bulk buttons stay usable: another action is queued behind it, and the panel says so.

### 5. Visible from other pages

The Jobs page lists a running or waiting bulk operation under the jobs, "Relink from the Torrents page: 7 of 23",
with **Cancel**, and links to the Torrents page. It isn't a registered job: it isn't scheduled, has no history and
doesn't count for notifications.

## Tests

- `BulkOperationQueueTests`: one at a time in order, progress counts, cancel after the current torrent, the last 10
  kept, shutdown marks a running one interrupted.
- `BulkActionRunnerTests`: each action, a failure in the middle doesn't stop the rest, the summary text.
- `LibraryWriteGateTests` or runner tests: a bulk relink and a Link run never work on files at the same time.
- `TorrentsPageTests`: the POST returns at once and the panel shows the operation; leaving and coming back still
  shows it; **Dismiss**; a missing category is still refused in the request.
- `JobsPageTests`: a running bulk operation is listed with **Cancel**.

## Documentation

- `docs/user-guide.md`, the Torrents page: bulk actions run in the background, the progress panel, **Cancel**, and
  that the page can be left meanwhile.

## Release notes

- Changed: bulk actions on the Torrents page run in the background with their progress shown above the list, and
  keep going when the page is left. Their summary stays until dismissed.
- Fixed: leaving the Torrents page during a bulk relink, unlink or category change stopped it halfway.
- Upgrading: nothing to do.

## Out of scope

- The **Following** page's bulk actions, which are quick database changes.
- Keeping bulk operations across a restart: each torrent's result is already recorded, and a restart interrupts
  jobs the same way.
