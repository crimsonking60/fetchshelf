# Dry run from the Jobs page, with what it would select listed — implementation plan

## Context

Autograbs, snatchlists, Hardcover lists, `[follow]` and `[upgrade]` each have `dry_run = true`: the job searches
and decides, writes nothing, and logs what it would have selected. Trying a new or changed block therefore means
editing `config.toml`, restarting, waiting for the run, reading the log, then editing and restarting again to turn
it off. The run's result on the Jobs page only gives counts ("Dry run: 4 would have been selected"); which four is
only in the log.

The job model has room for this since PR #43: runs are stored (`JobRuns`), triggered by name with a `JobTrigger`,
and shown with their summary and counts on the Jobs page and the job's history page.

## Change

### 1. A dry-run variant of a job

- `IJobRegistry.Register` gains an optional `dryRunAsync` delegate of the same shape as `runAsync`. A job that has
  one shows a **Dry run** button next to **Run now** on the Jobs page.
- `TriggerAsync(name, JobTrigger.DryRun)` runs `dryRunAsync` instead, under the same rules (one run of a job at a
  time, cancellable, recorded). `JobTrigger.DryRun` is new, shown as "Dry run" in the history's trigger column.
- A dry run doesn't count for the failing streak or the notification rules (`JobFailureState.Next`,
  `JobNotificationRules`): it says nothing about whether the real job works. It is still recorded.
- A dry run never chains: no Download run is queued after it.

### 2. The runners take the dry run as an argument

Today each runner reads `entry.DryRun` / `settings.DryRun`. Each `RunAsync` gains `bool? dryRun = null`, used
instead of the configured value when set: `AutograbberRunner`, `SnatchlistImporter`, `HardcoverListImporter`,
`FollowRunner`, `UpgradeRunner`. `Program.cs` (or `FetchshelfJobs`, after `plans/composition-root.md`) registers each
of these jobs with `dryRunAsync = ct => runner.RunAsync(…, dryRun: true, ct)`. A block configured with
`dry_run = true` stays a dry run either way, and its **Run now** says so.

### 3. What it would select, kept with the run

- `JobOutcome` gains `Details`: a short list of lines, at most 100, such as
  `"Selected: The Way of Kings by Brandon Sanderson (m4b, 1.2 GiB, cost free) — MAM 123456"`.
- The runners above add one line per torrent selected, or that would be in a dry run, and per torrent turned down
  for a reason worth seeing (duplicate of the library, no preferred format, cost not free), the latter capped at 50.
  Real runs fill it too, which makes the history useful beyond dry runs.
- `JobRunEntity.DetailsJson` (migration, nullable) stores them; `JobRunStore` writes and reads them.
- The job's history page shows a run's details under its row (htmx expand), with MAM ids linking to the Search
  page's detail view.

## Tests

- `JobRegistryTests`: `TriggerAsync(…, DryRun)` runs the dry-run delegate; a job without one refuses it; a failing
  dry run doesn't start a failing streak or a notification; the trigger is recorded.
- Runner tests (autograb, Hardcover list, follow, upgrade, snatchlist): `dryRun: true` overrides a configured
  `false` and writes nothing; details list what would be selected; real runs list what was selected.
- `JobRunStoreTests`: details round-trip; old rows without them read as empty.
- `JobsPageTests`: the **Dry run** button only for jobs that have one; a block configured `dry_run = true` shows
  only one button.
- `JobsPageTests` (the history page): details shown under the run.

## Documentation

- `docs/user-guide.md`, Jobs page: **Dry run**, and the details under a run in its history.
- `docs/configuration.md`, each `dry_run` row: "or press **Dry run** on the Jobs page".

## Release notes

- Added: a **Dry run** button on the Jobs page for autograbs, snatchlists, Hardcover lists, Follow and Upgrade, and
  a job's history lists what each run selected or would have selected.
- Upgrading: nothing to do.

## Out of scope

- Editing a block's settings on the page before a dry run.
