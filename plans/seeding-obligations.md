# Seeding obligations: which torrents are still unsatisfied — implementation plan

## Context

MAM counts, per account, snatches that are seeding but not yet satisfied, not seeding and not satisfied, and
hit-and-runs. Fetchshelf reads those counts (`MamAccountState.SnatchKinds`), shows them on the Account page, sends
`hit_and_run` when that count grows, and budgets downloads by the unsatisfied count. What it doesn't show is *which*
torrents they are. Removing an unsatisfied torrent from qBittorrent — by hand, by a client-side rule acting on the
`on_cleaned`/`on_unlinked` category, or after **Unlink** — is what turns it into a hit-and-run.

MAM's snatch lists say which: `GET /json/loadUserDetailsTorrents.php?type=…` with `seedUnsat` (seeding, not
satisfied) and `inactUnsat` (not seeding, not satisfied), which `MamClient.GetSnatchListAsync` already reads for
`[[snatchlist]]` (`MamSnatchlistKind.SeedingUnsat`, `InactiveUnsat`). These lists are short: together they hold at
most the account's unsatisfied limit. Anything not on them was satisfied or isn't this account's snatch.

(The review's 5.8 also gave `mark_removed` and `on_invalid_torrent` a home here; those were built separately in
PR #39.)

## Change

### 1. The Seeding job

Optional, with a `[seeding]` table:

| Setting | Default | Description |
|---|---|---|
| `search_interval` | `360` | Minutes between runs, `0` to 43200. |
| `tag_unsatisfied` | unset | A qBittorrent tag put on Fetchshelf's torrents that are unsatisfied, and taken off once satisfied, so qBittorrent's own rules can keep them. |
| `warn_before_removal` | `true` | Warn on **Unlink** and on the Clean job's `on_cleaned` when the torrent is unsatisfied. |

Each run:

1. Takes `MamAccessGate` as `Seeding`, reads `seedUnsat` and `inactUnsat` (every page; the CDN host, as the
   snatchlist import does), and releases the gate.
2. Joins the rows to `Torrents` by MAM id.
3. Sets `TorrentEntity.SeedingState` (migration; `SeedingUnsatisfied`, `NotSeedingUnsatisfied`, `Satisfied`, or
   null when never checked) and `SeedingCheckedAt`. A torrent of this account's that was unsatisfied and is on
   neither list now is `Satisfied`.
4. With `tag_unsatisfied`, adds the tag to unsatisfied torrents and removes it from satisfied ones, in whichever
   client holds each (`QBittorrentConnectionResolver`), only where it changes.
5. Summary: "12 unsatisfied (2 not seeding), 3 satisfied since the last run".

Metadata-only records (from a snatchlist import) are included: they are the user's snatches too.

### 2. Shown where it matters

- **Torrents page:** a *Seeding* filter (Unsatisfied, Not seeding and unsatisfied, Satisfied) and a column marker.
- **Torrent page:** "Unsatisfied on MAM, not seeding — seed it to avoid a hit-and-run", with when it was checked.
- **Account page:** the unsatisfied counts link to the Torrents page filtered by them.
- **Unlink** on an unsatisfied torrent asks for confirmation ("MAM doesn't count it as satisfied yet; removing it
  from qBittorrent before then makes it a hit-and-run"). Unlink keeps the torrent in qBittorrent, so this is about what
  the `on_unlinked` setting and the user's own client rules may do next.
- **Clean job:** with `warn_before_removal`, a superseded copy that is still unsatisfied is retired as today (its files
  leave the library) but `on_cleaned` is not applied to it until it is satisfied; the next runs apply it then. The
  log and the Errors page say why it waits.

### 3. A notification

`unsatisfied_not_seeding`: a torrent became *not seeding and unsatisfied* since the last run, with its title, so
it can be restarted before it turns into a hit-and-run. Merged per run like other events. Off unless a `[[notify]]`
block lists it.

## Check first

- The wire values and paging of `seedUnsat`/`inactUnsat` with a real account (the snatchlist import already uses the
  same endpoint with other types, and `MamSnatchlistKind` names both).
- How long MAM's answer is cached on the CDN (`cacheTime` parameter): a run every six hours is plenty.

## Tests

- `SeedingStateCalculatorTests` (Core, pure): rows → states, the "on neither list now" rule, metadata-only records.
- `SeedingRunnerTests`: the gate is taken; states and `SeedingCheckedAt` written; tags added and removed only on
  change; a torrent in no client is skipped for tagging.
- `CleanerTests`: an unsatisfied copy is retired without `on_cleaned`, which is applied once it is satisfied.
- Torrents page filter; Unlink confirmation.
- Notification rules: `unsatisfied_not_seeding` once per torrent per change.

## Documentation

- `docs/configuration.md`: `[seeding]`, and `unsatisfied_not_seeding` among the events.
- `docs/user-guide.md`: what satisfied means on MAM (pointing to MAM's own rules, without copying them), the filter,
  the warnings, and using `tag_unsatisfied` in qBittorrent.

## Release notes

- Added: the optional Seeding job shows which torrents MAM counts as unsatisfied, on the Torrents page and each
  torrent's page; it can tag them in qBittorrent, holds back `on_cleaned` for them, and warns before **Unlink**.
- Added: the `unsatisfied_not_seeding` notification.
- Upgrading: nothing to do; the job runs only with a `[seeding]` table.

## Out of scope

- Seeding time per torrent from qBittorrent against MAM's requirement: MAM's lists already say which torrents are
  satisfied, and MAM's rule may change.
- Restarting stopped torrents automatically.
