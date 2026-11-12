# Daily account snapshots: ratio, upload and bonus points over time — implementation plan

## Context

The Account page (PR #44) shows the account as MAM last answered: ratio, uploaded, downloaded, wedges, unsatisfied
count and limit, bonus points, class. Every read replaces the last one (`MamClient.LastAccountState`), so nothing
shows whether the ratio is rising or falling, how fast points accrue, or what a month of downloads did to the
budget. The plan for the Account page left out "store one snapshot a day" as a follow-up.

Account reads happen anyway: every MAM job reads the account (cached for 60 seconds), and `MamClient` raises
`AccountStateRead` for each, which `HitAndRunWatcher` already listens to.

## Change

### 1. Storing them

- `AccountSnapshots` table (migration): `Day` (UTC date, `yyyy-MM-dd`, unique), `TakenAt`, `Ratio`, `UploadedBytes`,
  `DownloadedBytes`, `Wedges`, `UnsatCount`, `UnsatLimit`, `BonusPoints` (nullable), `ClassName` (nullable).
- `AccountSnapshotRecorder` (Data), subscribed to `AccountStateRead` like `HitAndRunWatcher`: hands the read to a
  background write (the event fires on the MAM call's thread). The first read of a UTC day inserts that day's row;
  later reads that day update it, so the row holds the day's last value. Writes are skipped when the day's row was
  written less than 10 minutes ago, so a busy day costs a few writes.
- No extra MAM call: a day without any account read has no row.
- Kept for two years; older rows deleted by the recorder once a day.

### 2. Showing them

- Account page, new section "Over time": the last 90 days as small inline SVG line charts (server-rendered, no chart
  library): ratio, uploaded, bonus points, wedges; and a table of the last 12 months' month-end values with the change
  from the month before (uploaded +120 GiB, downloaded +40 GiB, points +30,000).
- When `plans/statistics-and-metrics.md` is built, its page links here for these.

### 3. Export

The snapshots download as CSV from the section ("Download as CSV").

## Tests

- `AccountSnapshotRecorderTests`: first read of a day inserts, later ones update, the 10-minute skip, a new UTC day
  inserts a new row, pruning past two years, a failing write is logged and doesn't throw into the MAM call.
- `AccountSnapshotChartTests` (pure SVG builder): points scaled to the box, gaps for days without a row, one value.
- `AccountPageTests`: the section with no snapshots ("Nothing recorded yet"), with a few, and the CSV.

## Documentation

- `docs/user-guide.md`, Account page: "Over time", that a day appears once any job has read the account that day.

## Release notes

- Added: the Account page keeps one snapshot of the account a day and shows ratio, upload, bonus points and wedges
  over the last 90 days, and month by month.
- Upgrading: nothing to do; history starts on the first day after upgrading.

## Out of scope

- Snapshots more often than daily.
- Backfilling history from before the upgrade (MAM's API doesn't give past values).
