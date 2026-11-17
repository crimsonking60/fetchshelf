# Seeding obligations: which torrents still need seeding — implementation plan

## Context

MAM counts, per account, snatches that are seeding but not yet satisfied, not seeding and not satisfied, and
hit-and-runs. Fetchshelf reads those counts (`MamAccountState.SnatchKinds`, from `jsonLoad.php?snatch_summary`),
shows them on the Account page, sends `hit_and_run` when that count grows, and budgets downloads by the
unsatisfied count. What it doesn't show is *which* torrents they are. Removing an unsatisfied torrent from
qBittorrent — by hand, by a client-side rule acting on the `on_cleaned`/`on_unlinked` category, or after **Unlink** —
is what turns it into a hit-and-run.

An earlier version of this plan read MAM's snatch lists (`/json/loadUserDetailsTorrents.php`, types `seedUnsat` and
`inactUnsat`), which name exactly those torrents. That endpoint isn't among the parts of MAM its API documentation
authorizes automating. `[[snatchlist]]`, which used it, was removed for that reason (PR #77). This plan uses only
what is authorized and what qBittorrent knows:

- **qBittorrent** (`/api/v2/torrents/info`, already read by the Link job) gives each torrent's `seeding_time`
  (seconds spent seeding after it completed), `completion_on` and `added_on`, which Fetchshelf doesn't read yet.
- **`jsonLoad.php?snatch_summary`** gives MAM's own counts, which Fetchshelf already reads.
- **The search** answers `my_snatched` per torrent, but nothing about whether a snatch is satisfied.

So Fetchshelf can't know which torrents MAM counts as unsatisfied; it can *estimate* it from the seeding time
against MAM's seeding requirement, and check the estimate against MAM's counts. Everything below says
"looks unsatisfied", never "is".

## Check first

- **MAM's seeding requirement**, from MAM's rules: how much seeding time satisfies a snatch, and within what period
  of the snatch it must be reached before it counts as a hit-and-run. Believed to be 72 hours within 30 days;
  confirm it, since the defaults below come from it. The docs point to MAM's rules rather than copying them.
- **qBittorrent's `seeding_time`** on the live client: present in the versions in use, counts only time spent
  seeding (not stalled or paused time), and survives a restart of qBittorrent. If it doesn't, the estimate falls
  back to the time since `completion_on` while the torrent was in a seeding state, sampled per run, and the plan
  says so.

## Change

### 1. The Seeding job

Optional, with a `[seeding]` table:

| Setting | Default | Description |
|---|---|---|
| `seed_hours` | from the check (72) | Seeding time MAM requires before a snatch is satisfied. |
| `within_days` | from the check (30) | Days after the snatch in which `seed_hours` must be reached. |
| `interval` | `60` | Minutes between runs, `0` to 43200. |
| `tag_unsatisfied` | unset | A qBittorrent tag put on Fetchshelf's torrents that look unsatisfied, and taken off once they look satisfied, so qBittorrent's own rules can keep them. |
| `warn_before_removal` | `true` | Warn on **Unlink** and hold back the Clean job's `on_cleaned` for torrents that look unsatisfied. |

The job doesn't talk to MAM at all, so it takes no MAM gate. Each run:

1. Reads every torrent from each qBittorrent client (one `torrents/info` call per client, as the Link job does),
   with `seeding_time`, `completion_on`, `added_on` and the state.
2. Joins them to `Torrents` by info hash. Only Fetchshelf's torrents with a MAM id count; metadata-only records,
   with no torrent in a client, are left out (Fetchshelf can't tell their seeding time).
3. `SeedingEstimator` (Core, pure) gives each a state from the seeding time, the time since completion and the
   requirement:
   - **Satisfied:** seeding time at least `seed_hours`.
   - **Seeding, unsatisfied:** less than that, seeding now, still within `within_days` of the snatch (as built: when
     qBittorrent added the torrent, since MAM's period starts at the snatch, not at completion); with the
     hours still needed and the date by which they must be reached.
   - **Not seeding, unsatisfied:** less than that, not seeding now (stopped, errored, missing files, or no longer in
     the client), still within the period.
   - **Past the period:** never reached `seed_hours` within `within_days`: likely counted as a hit-and-run.
   A torrent that was satisfied stays satisfied, even after it leaves the client.
4. Stores the state, the seeding time and when it was checked (`TorrentEntity.SeedingState`, `SeedingSeconds`,
   `SeedingCheckedAt`; migration).
5. With `tag_unsatisfied`, adds the tag to torrents that look unsatisfied and removes it from those that look
   satisfied, in whichever client holds each (`QBittorrentConnectionResolver`), only where it changes.
6. Summary: "12 look unsatisfied (2 not seeding), 3 satisfied since the last run; MAM counts 13 (2 not seeding)".

### 2. Checked against MAM's counts

The Account page already shows MAM's counts from `jsonLoad.php`. Next to them, the estimate: "Fetchshelf's torrents:
11 seeding and 2 not seeding look unsatisfied." When they differ, a short note says why they can: snatches made
outside Fetchshelf or in another client, torrents Fetchshelf only has as metadata, or seeding time counted
differently by MAM than by qBittorrent. The estimate is a guide; MAM's counts are what matter.

### 3. Shown where it matters

- **Torrents page:** a *Seeding* filter (Looks unsatisfied, Not seeding and unsatisfied, Past the period, Satisfied)
  and a column marker, with the hours still needed.
- **Torrent page:** "Seeded 41 of 72 hours; needs 31 more by 2026-12-14", or "Not seeding, and looks unsatisfied:
  seed it again to avoid a hit-and-run", with when it was checked.
- **Account page:** the counts link to the Torrents page filtered by them.
- **Unlink** on a torrent that looks unsatisfied asks for confirmation ("It has seeded 41 of the 72 hours MAM
  requires; removing it from qBittorrent before then can make it a hit-and-run"). Unlink keeps the torrent in
  qBittorrent, so this is about what `on_unlinked` and the user's own client rules may do next.
- **Clean job:** with `warn_before_removal`, a superseded copy that looks unsatisfied is retired as today (its files
  leave the library) but `on_cleaned` is not applied to it until it looks satisfied; the next runs apply it then. The
  log says why it waits (as built: the log only, not the Errors page, since nothing is wrong).

### 4. A notification

`unsatisfied_not_seeding`: a torrent began to look *not seeding and unsatisfied* since the last run, with its title
and the hours still needed, so it can be restarted before it turns into a hit-and-run. Merged per run like other
events. Off unless a `[[notify]]` block lists it.

## Tests

- `SeedingEstimatorTests` (Core, pure): each state at the boundaries of `seed_hours` and `within_days`; satisfied
  stays satisfied after leaving the client; a torrent gone from the client within the period is not seeding.
- `SeedingRunnerTests`: no MAM call; states, seconds and `SeedingCheckedAt` written; metadata-only records left out;
  tags added and removed only on change; a torrent in no client is skipped for tagging.
- `QBittorrentClientTests`: `seeding_time`, `completion_on` and `added_on` read, and missing ones tolerated.
- `CleanerTests`: a copy that looks unsatisfied is retired without `on_cleaned`, which is applied once it looks
  satisfied.
- Account page: the estimate next to MAM's counts, and the note when they differ. Torrents page filter; Unlink
  confirmation.
- Notification rules: `unsatisfied_not_seeding` once per torrent per change.

## Documentation

- `docs/configuration.md`: `[seeding]`, and `unsatisfied_not_seeding` among the events.
- `docs/user-guide.md`: that the states are an estimate from qBittorrent's seeding time against MAM's requirement
  (pointing to MAM's own rules, without copying them), why it can differ from MAM's counts, the filter, the
  warnings, and using `tag_unsatisfied` in qBittorrent.

## Release notes

- Added: the optional Seeding job estimates, from qBittorrent's seeding time, which of Fetchshelf's torrents still
  need seeding to satisfy MAM, and shows it on the Torrents page, each torrent's page and next to MAM's counts on the
  Account page. It can tag them in qBittorrent, holds back `on_cleaned` for them, and warns before **Unlink**.
- Added: the `unsatisfied_not_seeding` notification.
- Upgrading: the database is upgraded; the job runs only with a `[seeding]` table.

## Out of scope

- MAM's own per-torrent satisfaction: only the snatch lists say it, and they aren't authorized for automation.
- Torrents seeded in another client, or snatched before Fetchshelf knew them.
- Restarting stopped torrents automatically.
