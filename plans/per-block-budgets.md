# Buffers and active-download caps for every grab block, and wedge downloads outside the ratio budget — implementation plan

## Context

Two things in how the budgets apply don't hold for every kind of block.

**Per-block buffers and caps exist only for autograbs.** `[[autograb]]` can set `unsat_buffer`, `wedge_buffer` and
`max_active_downloads`. They apply twice:

- at selection, in the run budget: `AutograbberRunner.RunAsync` uses `entry.UnsatBuffer ?? global` and
  `SelectionQueue.ActiveForAutograbAsync` for the cap;
- at download, per torrent: `SafetyBuffers.UnsatBufferFor(grabberName)` / `WedgeBufferFor(grabberName)` look the
  selection's `GrabberName` up among the autograbs (`AutograbBuffers`).

`[[hardcover_list]]`, `[follow]` and `[upgrade]` have none of the three. `Program.cs` passes the global
`settings.UnsatBuffer` to their runners, and each calls `SelectionBudget.Compute(…, maxActiveDownloads: null, …)`.
A Hardcover list of 300 books can fill every slot down to the global buffer, and its wedge downloads use the global
`wedge_buffer`. The planned `[[file_list]]` (`plans/file-based-lists.md`) would inherit the same gap.

**A `wedge` download counts against the ratio budget although it never costs ratio.** `DownloadBudget.IsFreeCost`
leaves out `UseWedge` and `TryWedge`, so `FitsRatioBudget` holds both back when the ratio budget is spent, and
`SelectionQueue.InFlightBytesAsync` counts both in the bytes in flight. For `TryWedge` that is right: it may download
without a wedge. For `UseWedge` it is not: `DownloadRunner` either spends a wedge or keeps the torrent queued
(`WedgeDecision.Fail` → `WedgeUnavailable`); it never downloads it on ratio. Since the ratio budget was corrected
(PR #32) the budget is often 0, so `wedge` blocks stop working exactly when they are most useful. The same holds for a
`TryWedge` download that did get a wedge: it no longer costs ratio, but stays in the bytes in flight until it
finishes.

## Change

### 1. Buffers by grabber name, for every kind of block

- `AutograbBuffers` becomes `BlockBuffers(string GrabberName, int? UnsatBuffer, int? WedgeBuffer)`, and
  `SafetyBuffers` takes a list of them plus a prefix rule for Follow: a selection whose grabber name starts with
  `Follow: ` (`FollowRunner.GrabberName`) uses `[follow]`'s buffers. Exact names are matched first.
- `Program.cs` builds the list from `[[autograb]]` (name), `[[hardcover_list]]` (its job name), `[[file_list]]` when
  that exists, `[upgrade]` (`JobNames.Upgrade`) and `[follow]` (the prefix).

### 2. Settings

`unsat_buffer`, `wedge_buffer` and `max_active_downloads` in `[[hardcover_list]]`, `[follow]` and `[upgrade]`, with the
same meaning and range checks as on `[[autograb]]`. Unlike an autograb, they don't need a `name`: a Hardcover list's
job name is unique already, and `[follow]` and `[upgrade]` have one each.

### 3. The run budget

- `SelectionQueue.ActiveForAutograbAsync` becomes `ActiveForGrabberAsync(IReadOnlyCollection<string> names | prefix)`:
  selections not removed and not started, for those grabber names. For Follow, every `Follow: …` name together.
- The list importer, `FollowRunner` and `UpgradeRunner` take their block's buffer and cap (falling back to the global
  buffer, as `AutograbberRunner` does) and pass them to `SelectionBudget.Compute`/`Explain`. `Program.cs` stops
  handing them `settings.UnsatBuffer` directly.

### 4. `wedge` downloads outside the ratio budget

- `DownloadBudget.FitsRatioBudget`: `UseWedge` fits whatever the budget. `IsFreeCost` itself is unchanged; a new
  `CostsRatio(cost)` says which costs may spend ratio: `Ratio` and `TryWedge`.
- `SelectedTorrentEntity.StartedWithWedge` (bool, migration, default false), set by `DownloadRunner` when it starts a
  torrent with a wedge. `InFlightBytesAsync` counts a started selection only if its cost may spend ratio and it
  didn't start with a wedge. Existing rows (all false) count as today until they finish.
- `DownloadQueueForecast`, which the Account page uses to say how many queued selections would start now, follows
  `FitsRatioBudget`, so it changes with it.

## Tests

- `SafetyBuffersTests`: exact names, the `Follow: ` prefix, the global fallback.
- `DownloadBudgetTests`: `UseWedge` fits a zero budget; `TryWedge` and `Ratio` don't.
- `SelectionQueueTests`: in-flight bytes leave out `UseWedge` and a `TryWedge` started with a wedge.
- `DownloadRunnerTests`: with a ratio budget of 0, a `wedge` selection starts with a wedge; with no wedge above the
  buffer it stays queued as `WedgeUnavailable`, never on ratio; `StartedWithWedge` is set.
- `HardcoverListImporterTests`, `FollowRunnerTests`, `UpgradeRunnerTests`: their own `unsat_buffer` lowers the run
  budget; `max_active_downloads` stops selection once that many are waiting.
- Config tests for the new settings and their ranges.

## Documentation

- `docs/configuration.md`: the three settings in each of the three sections; in "Cost", that `wedge` is never held
  back by the ratio budget, only by `wedge_buffer`, while `try_wedge` is.
- `docs/configuration.md`, top-level `unsat_buffer`/`wedge_buffer`: "Can be overridden per autograb" → "per block".

## Release notes

- Added: `unsat_buffer`, `wedge_buffer` and `max_active_downloads` in `[[hardcover_list]]`, `[follow]` and
  `[upgrade]`.
- Fixed: downloads with `cost = "wedge"` are no longer held back when the ratio budget is spent, since they never
  cost ratio; and a `try_wedge` download that got a wedge no longer counts against the ratio budget while it
  downloads.
- Upgrading: nothing to do. More `wedge` downloads may start than before when the ratio budget is low; each still
  needs a wedge above `wedge_buffer`.

## Out of scope

- The user's own VIP expiry against the download's size: MAM's account answer would have to give the expiry first.
