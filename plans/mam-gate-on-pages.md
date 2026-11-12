# MAM calls from pages take the MAM gate — implementation plan

## Context

`MamAccessGate` makes sure only one thing talks to MAM at a time. Every job takes it, and so do the Account page's
**Refresh** (`Pages/Account/Index.cshtml.cs`, `TryAcquireAsync` with `RefreshWait`) and a **Download** on the
Following page (`FollowService`, `TryAcquireAsync` with `GateWait` = 10 s, then "MAM is busy with … ; try again in
a moment").

Four page actions still call `MamClient` without it:

| Where | Call |
|---|---|
| `Pages/Index.cshtml.cs` `OnGetResultsAsync` | the search itself |
| `Pages/Index.cshtml.cs` `OnPostDownloadAsync` | the lookup by id, then `SelectionRunner.ProcessCandidateAsync` |
| `Pages/Index.cshtml.cs` `OnPostImportAsync` | the lookup by id |
| `Pages/Search/Detail.cshtml.cs` `OnGetAsync` and `OnGetCoverAsync` | the lookup by id, the cover image |

The politeness delays inside `MamClient` still space the requests, so this is not dangerous to the account. The
real problem is the download: it runs selection while an autograb may be running selection on another
`DbContext`, so two copies of the same book can both pass the duplicate check and both be queued.

## Change

### 1. One helper for page actions

`MamPageGate` in `Fetchshelf.Web`, a thin wrapper over `MamAccessGate.TryAcquireAsync`:

- `Task<IDisposable?> TryEnterAsync(string owner, CancellationToken ct)`, waiting `FollowService.GateWait`
  (moved to `MamPageGate.Wait`, and `FollowService` uses it from there).
- `string BusyMessage` — "MAM is busy with {owner}; try again in a moment.", the Following page's wording.

The Account page and `FollowService` keep their own calls; only the constant moves.

### 2. The Search page

- **Search** (`OnGetResultsAsync`): takes the gate as `Search page`. When busy, the results partial shows the busy
  message in place of results, with a **Try again** button that repeats the htmx request. The search is short, so
  holding the gate for it costs a running job one request's wait.
- **Download** (`OnPostDownloadAsync`): takes the gate as `Search page download` around the lookup *and*
  `ProcessCandidateAsync`, so selection never runs alongside a job's selection. Busy → the download feedback
  partial shows the busy message as an error; nothing is selected.
- **Import** (`OnPostImportAsync`): the same, around the lookup.

### 3. The search result page

`Search/Detail`: the lookup takes the gate as `Search page`. Busy → the page renders with "MAM is busy …" in place
of the details, and a reload link. The cover is fetched from MAM's image host, not the API: it stays outside the
gate (it's an image request the browser would otherwise make itself), which the code comment says.

### 4. The rule, written down

`MamAccessGate`'s summary and `CLAUDE.md` already say every MAM caller takes the gate. `docs/contributors-guide.md`,
"Working with MAM responsibly", gains one line: page actions take it with a short wait and say who is busy rather
than queueing behind a long job.

## Tests

- `IndexPageTests`: with the gate held by a fake owner, a search shows "MAM is busy with Autograb X" and sends no
  MAM request; a download selects nothing and says so; an import imports nothing.
- `SearchDetailPageTests`: busy gate → busy message, no MAM request.
- The existing download and search tests pass unchanged (the gate is free in them).

Tests use a short `MamPageGate.Wait` through a constructor parameter, so they don't wait 10 s.

## Documentation

- `docs/user-guide.md`, Search page: while a job is talking to MAM, a search or download waits up to 10 seconds,
  then says which job is busy.

## Release notes

- Fixed: a download from the Search page no longer runs at the same time as an autograb's selection, which could
  queue the same book twice. While a job is talking to MAM, the Search page waits a moment and then says which.
- Upgrading: nothing to do.

## Out of scope

- Moving the gate into `MamClient` so callers can't forget it: jobs hold the gate across many calls (a whole run),
  which a per-call gate inside the client can't express.
