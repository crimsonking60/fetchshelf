# Ratio budget: keep the ratio at `min_ratio` — implementation plan

## Context

`min_ratio` (default `2`) is documented as a hard floor: "Fetchshelf never starts a download that would take
your account below this ratio" (`docs/configuration.md`), and "A download that would push you below it is
skipped" (`docs/user-guide.md`). The Download job does not keep that promise.

`DownloadBudget.ComputeRatioBudgetBytes` (`src/Fetchshelf.Core.AutoDownloader/DownloadBudget.cs`) computes

```
budget = (uploaded − downloaded − in flight) / min_ratio
```

The ratio after downloading `x` more bytes is `uploaded / (downloaded + in flight + x)`. Keeping it at or above
`m` requires

```
x ≤ uploaded / m − downloaded − in flight
```

The two differ by `(downloaded + in flight) × (1 − 1/m)`. With `m > 1`, which includes the default, the code
allows more than the floor permits; with `m < 1` it allows less; at exactly `1` they agree.

Example with the default `min_ratio = 2`, uploaded 100 GB, downloaded 40 GB, nothing in flight (ratio 2.5):

| | Budget | Ratio after spending it |
|---|---|---|
| Current formula | (100 − 40) / 2 = 30 GB | 100 / 70 = 1.43 |
| Correct bound | 100 / 2 − 40 = 10 GB | 100 / 50 = 2.00 |

An account already below the floor (uploaded 100, downloaded 60, ratio 1.67) gets a 20 GB budget today, where
the correct bound gives 0.

The existing test (`DownloadBudgetTests.RatioCost_OversizedTorrent_DoesNotFitBudget`) uses
`downloadedBytes: 0` and no bytes in flight, where both formulas give the same answer, so it never caught this.
The `DownloadRunnerTests.RunAsync_OverRatioBudget_SkipsRatioCostTorrent` account also has `downloaded_bytes: 0`.

## Where the budget is used

Only the Download job. `DownloadRunner.RunAsync` reads the account state once per run, sums the sizes of
started, unfinished ratio-spending selections (`SumInFlightBytesAsync`), computes the budget, and
`DownloadRunBudget` subtracts each started torrent's size from it for the rest of the run.
`DownloadBudget.FitsRatioBudget` compares a torrent's size with what is left. Autograbs, Hardcover lists and the
Search page only queue selections; they don't look at the ratio, so nothing else changes.

## Change

1. **Formula.** In `ComputeRatioBudgetBytes`:

   ```csharp
   var budget = uploadedBytes / minRatio - downloadedBytes - bytesInFlight;
   ```

   computed in `double` (the account values arrive as `double` already), floored at 0 and capped at
   `long.MaxValue` before the cast to `long`. The cap keeps the cast defined until the range checks for
   `min_ratio` land: today `min_ratio = 0` divides by zero, gives infinity, and casting
   infinity to `long` is undefined. The range check itself is not part of this change.

   Update the doc comment to the new formula, and say why it is that shape: it is the largest `x` for which
   `uploaded / (downloaded + in flight + x) ≥ min_ratio`.

2. **Log the budget once per run.** `DownloadRunner` currently logs only "stays queued: downloading it would
   take the ratio below min_ratio" per torrent. Add one Information line when the run starts:
   `Ratio budget for this run: 10.0 GiB (uploaded 100.0 GiB, downloaded 40.0 GiB, 0 B in flight, min_ratio 2)`,
   formatted by a small helper next to the formula (there is no shared size formatter). With fewer downloads starting after the upgrade, this is what a user
   needs to see why. Only when there are pending selections, as the account state is only fetched then.

Nothing else moves: `FitsRatioBudget`, `DownloadRunBudget` and `SumInFlightBytesAsync` stay as they are.

## What users will notice

- With the default `min_ratio = 2`, fewer `ratio`-cost downloads start: the budget shrinks by about
  `(downloaded + in flight) / 2`. Torrents that no longer fit stay queued on the **Selected** page, as they do
  today when the budget runs out, and start once the ratio allows.
- An account already at or below `min_ratio` starts no `ratio`-cost downloads at all.
- Free downloads (VIP, freeleech, personal freeleech) are unaffected.
- With `min_ratio` below 1, the budget gets larger than it is today, up to the true floor.

## Tests

`tests/Fetchshelf.Core.AutoDownloader.Tests/DownloadBudgetTests.cs`, as a `[Theory]` over
`ComputeRatioBudgetBytes`:

| uploaded | downloaded | in flight | min_ratio | budget |
|---|---|---|---|---|
| 100 | 40 | 0 | 2 | 10 |
| 100 | 40 | 5 | 2 | 5 |
| 100 | 60 | 0 | 2 | 0 (below the floor) |
| 100 | 50 | 0 | 2 | 0 (exactly at the floor) |
| 100 | 0 | 0 | 2 | 50 |
| 100 | 40 | 0 | 1 | 60 |
| 100 | 40 | 0 | 0.5 | 160 |

Plus one property-style check: for a few accounts, spending the whole budget leaves
`uploaded / (downloaded + in flight + budget) ≥ min_ratio`, and one byte more would not. Plus one for the cap:
`min_ratio = 0` gives `long.MaxValue` rather than a negative or garbage value.

`tests/Fetchshelf.Data.AutoDownloader.Tests/DownloadRunnerTests.cs`: one end-to-end case with
`downloaded_bytes` set, where the old formula would start the torrent and the new one keeps it queued (for
example uploaded 100 MB, downloaded 40 MB, torrent 20 MB, `min_ratio` 2: old budget 30 MB, new 10 MB).

## Documentation

- `docs/configuration.md`, the **Ratio budget** bullet: the formula becomes
  `uploaded / min_ratio − downloaded − bytes already downloading`, with one sentence saying it is the most that
  can be downloaded while the ratio stays at `min_ratio`.
- `docs/user-guide.md`, the **Ratio** bullet under "How a run works out how much it may download": the same
  formula, and a short worked example (uploaded 100 GB, downloaded 40 GB, `min_ratio = 2`: 10 GB).
- The `min_ratio` descriptions stay: the change makes them true.

## Release notes

Under **Fixed**: the ratio budget let the account fall below `min_ratio`. Under **Upgrading**: with
`min_ratio` above 1 (the default is 2), fewer downloads that cost ratio start than before; they wait on the
**Selected** page until the ratio allows them. Nothing to change in the configuration, and no database
migration. Written at release time, not in this change.

## Out of scope

- Range checks for `min_ratio` and the other numeric settings, planned separately.
- Wedge costs counting against the ratio budget: with the smaller budget this matters more,
  and is worth doing soon after, separately.
- The in-flight sum counts the full size of a started download until it finishes, while MAM's `downloaded`
  already includes the part that has arrived. That double-counts on the safe side and stays as it is.
