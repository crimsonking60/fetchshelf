# Job runs: kept across restarts, run soon after start, and cancellable — implementation plan

## Context

Every background job is a delegate registered by name in `IJobRegistry` (`Fetchshelf.Core/Jobs/JobRegistry.cs`),
which keeps one `JobRunRecord` per job in memory: when the last run started, its result, and an error text.
`IntervalJobScheduler` (`Fetchshelf.Web/Jobs`) waits a full interval, then triggers the job, in a loop. The Jobs
page (`Pages/Jobs`) shows the last run and offers **Run now**. This falls short in ways a user sees:

- **Nothing survives a restart.** After an upgrade or a container restart, every job says *Never run*, and there
  is no history at all: whether last night's autograb runs found anything, or when the Link job last failed, is
  only in the log.
- **The first run waits a whole interval.** With `search_interval = 30`, nothing is searched, downloaded or
  linked for half an hour after a restart. With `link_interval = 1440`, a restart postpones linking by a day.
- **A running job can't be stopped.** A Hardcover list run working through 300 books, or a Link run stuck on a
  slow network share, runs to its end. The delegates already take a `CancellationToken`, but nothing outside
  the host's shutdown ever cancels it.
- **A run's outcome is one line.** *Success* says nothing about what was done; the counts each runner already
  works out (`LinkRunSummary`, `CleanRunSummary`, the autograb's selected count, `SnatchlistRunSummary`, the
  Hardcover and Audiobookshelf summaries, `DownloadRunSummary`) only reach the log.
- **What started a run isn't recorded.** A Download run started by an autograb, by **Run now** and by its own
  timer look the same.

## Change

### 1. A run's outcome

A job delegate returns what it did instead of nothing:

```csharp
public sealed record JobOutcome(string Summary, IReadOnlyDictionary<string, long>? Counts = null);

void Register(string name, Func<CancellationToken, Task<JobOutcome?>> runAsync, string? description = null);
```

- `Summary` is one readable line, written where the job is registered from the runner's summary: "3 torrents
  linked, 1 failed", "312 search results, 2 selected", "Nothing to do: this run's limit is 0 selections".
- `Counts` holds the same numbers by name (`linked`, `failed`, `selected`, …) for the history page and later for
  notifications. Runners don't change; `Program.cs` maps each runner's existing summary record.
- `JobCompletedWithErrorsException` stays as it is: it marks a run that did its work but had items fail. Its
  message becomes the run's summary.
- The autograb runner's `int` return grows into a small `AutograbRunSummary(ResultCount, Selected, BudgetNote)`,
  so its job can say how many results were read as well as how many were selected. The removal check from
  `mark_removed` adds `CheckedForRemoval` and `MarkedRemoved`.

### 2. A `JobRuns` table

New entity `JobRunEntity` (migration `AddJobRuns`):

| Column | Type | Meaning |
|---|---|---|
| `Id` | int | key |
| `JobName` | string, indexed | the registered name |
| `StartedAt` | DateTimeOffset | |
| `FinishedAt` | DateTimeOffset? | null while running, or when the host stopped mid-run |
| `Result` | enum | `Running`, `Success`, `CompletedWithErrors`, `Failure`, `Cancelled`, `Interrupted` |
| `Trigger` | enum | `Schedule`, `Startup`, `Manual`, `Chained` |
| `Summary` | string? | the outcome's line, or the error message |
| `CountsJson` | string? | the outcome's counts |

Core stays free of EF Core: the registry writes through an interface in `Fetchshelf.Core/Jobs`,

```csharp
public interface IJobRunStore
{
    Task<long> StartedAsync(string jobName, DateTimeOffset startedAt, JobTrigger trigger);
    Task FinishedAsync(long runId, DateTimeOffset finishedAt, JobRunResult result, string? summary, IReadOnlyDictionary<string, long>? counts);
    Task<IReadOnlyDictionary<string, JobRunRecord>> LastRunsAsync();
}
```

implemented by `JobRunStore` in `Fetchshelf.Data`, with its own `DbContext` per call (the registry is a singleton,
and a run must be recorded even when the job's own scope failed). A failure to write a run record is logged and
never fails the job.

- **At startup** the registry loads the last run of every job, so the Jobs page shows it straight away. A run
  left `Running` from before the restart is marked `Interrupted`, with `FinishedAt` left empty: when it stopped is
  not known, and the time of the restart would count the downtime as run time.
- **Retention:** after each run, rows of that job beyond the newest 200 are deleted. That is days of history for
  a job every few minutes, and months for a daily one, at a few kilobytes per job.
- `JobRunRecord` grows `FinishedAt`, `Trigger` and `Summary`. `JobRunResult` gains `Cancelled` and `Interrupted`.

### 3. The first run after start

`IntervalJobScheduler` works out the first trigger from the job's last recorded start instead of waiting a full
interval:

- **Run before, and not yet due:** the first run is at the last start plus the interval. A restart doesn't change
  a job's rhythm.
- **Never run, or overdue:** the first run is soon after start, staggered so jobs don't all reach MAM at once:
  the Link job after 30 seconds, then each MAM-talking job (autograbs, snatchlists, Hardcover lists, in
  configuration order) 30 seconds after the previous one, and the Download job after the last of them. The
  `MamAccessGate` already keeps them from overlapping; the stagger just keeps the order predictable and the log
  readable.
- **A restart loop can't hammer MAM:** a job that started less than one interval ago, even if that run was
  interrupted, waits for its interval as above.
- Runs started this way are recorded with the trigger `Startup`.

The scheduler gets the last start from the registry (loaded from the store) and the stagger position from
`Program.cs`, which already registers the schedulers in order. The Jobs page's *Next run in* shows the real first
run.

### 4. Cancel

- The registry keeps a `CancellationTokenSource` per running job, linked to the token the run was started with.
  `IJobRegistry.Cancel(name)` cancels it; it returns false when the job isn't running.
- A queued extra run (`TriggerOrQueueAsync`) is dropped too: cancelling means "stop this job now", not "skip to the
  next queued run".
- A run that ends with an `OperationCanceledException` from that source is recorded as `Cancelled`, with "Cancelled
  from the Jobs page" as its summary. A cancellation from host shutdown is recorded as `Interrupted`.
- **Each runner is checked for where a cancellation lands**, and given explicit cancellation points where landing
  mid-step would leave a half-done step behind:
  - **Download:** checks the token only between torrents. A torrent whose file was fetched (and a wedge possibly
    spent) is always added to qBittorrent before the run stops.
  - **Link:** already commits its batch in a `finally` with `CancellationToken.None`; a torrent whose files are
    being placed finishes placing before the run stops.
  - **Clean:** stops between copies; a copy being retired finishes.
  - **Autograb, snatchlist, Hardcover:** stop between MAM requests. A selection being written finishes.
  - **Audiobookshelf:** stops between books.
- Cancelling a job doesn't cancel the jobs it started (Link's Clean run, an autograb's Download run): those are
  their own runs, cancellable on their own.

### 5. What triggered a run

`TriggerAsync` and `TriggerOrQueueAsync` take a `JobTrigger`. The scheduler passes `Schedule` (or `Startup`),
the Jobs page `Manual`, and a job starting another (Link → Clean, autograb or Hardcover list → Download) `Chained`.
The Search page's immediate download (`download_after_selection`) passes `Manual`.

### 6. The Jobs page

- **Result** shows the run's summary: "Success: 3 torrents linked" instead of "Success".
- **Last run** shows when it started and how long it took; while running, "Running for 2m 10s", polled as today.
- A **Cancel** button replaces **Run now** while the job runs, with no confirmation (cancelling loses nothing
  that isn't redone on the next run).
- Each job name links to a new **history page** (`Pages/Jobs/History.cshtml`, `/jobs/history?job=Link`): the
  job's runs, newest first, 50 to a page, with start, duration, trigger, result and summary, and a filter for
  runs that didn't succeed. A run's counts show on hover over its summary.

## Tests

- `JobRegistry` with an in-memory `IJobRunStore` fake:
  - a run is recorded as started and finished, with its summary, counts and trigger;
  - a `JobCompletedWithErrorsException` and a failure are recorded with their messages;
  - `Cancel` on a running job cancels its token, records `Cancelled`, and drops a queued run;
  - `Cancel` on an idle job returns false;
  - a store that throws doesn't fail the job;
  - last runs from the store show in `GetAll()` at startup, and a `Running` row becomes `Interrupted`.
- `JobRunStore` against SQLite: write, read back the last run per job, retention keeps the newest 200.
- First-run timing as a pure function (`FirstRunPlanner`, Core): not due → last start + interval; never run →
  stagger; overdue → stagger; started within an interval and interrupted → last start + interval.
- `IntervalJobScheduler` with a fake `TimeProvider`: the first trigger comes at the planned time, then every
  interval.
- Runner cancellation:
  - Download: a token cancelled while a torrent is being added still adds it, then the run stops.
  - Link: a cancelled run commits what it linked.
- Migration test: `AddJobRuns` on a database from 2026.11.7.
- Web tests:
  - Jobs page shows the summary, a **Cancel** button for a running job, and a link to the history.
  - The history page lists runs newest first and filters to unsuccessful ones.
  - Posting **Cancel** cancels the job.

## Documentation

- `docs/user-guide.md`, Jobs page: the summary, **Cancel** and what is safe about it, the history page, and when a
  job first runs after a restart.
- `docs/configuration.md`: the intervals' description says the first run after a restart follows the last run,
  or comes within a few minutes of start for a job that is due.

## Release notes

**Added:** job history, **Cancel**, summaries. **Changed:** jobs run soon after start instead of one interval
later. **Upgrading:** a database migration adds the `JobRuns` table; no going back. Written at release time.

## Out of scope

- A maximum run time per job. Cancel covers the stuck run; a timeout needs a value per job that nobody can pick
  well yet.
- Showing job failures elsewhere than the Jobs page: see `plans/job-failures.md`.
- Moving the job registration out of `Program.cs` (review point A1). This plan only changes the lambdas that are
  there.
