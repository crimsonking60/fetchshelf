# `mark_removed` and `on_invalid_torrent`: make the two silent settings work — implementation plan

## Context

Two settings are accepted, validated and documented, and do nothing:

- **`mark_removed`** on `[[autograb]]`. `docs/configuration.md`: "For torrents Fetchshelf already stores: if the
  results show a gap where a stored torrent should be, ask MAM directly, and mark the torrent as removed from MAM if
  it is gone. It is accepted and validated, but nothing acts on it yet."
- **`on_invalid_torrent`** on `[[qbittorrent]]`, a table with an optional `category` and `tags` like `on_cleaned`
  and `on_unlinked`. Documented only as "accepted and validated, but nothing acts on it yet". Its meaning, carried
  over with the setting name: what to do in qBittorrent to a torrent whose MAM details can't be used because they
  hold a media type (or other reference value) Fetchshelf doesn't know.

A setting that is accepted and ignored is worse than a missing one: a user who sets it believes it works. The pieces
around them partly exist — `EventKind.RemovedFromMam` with an Events page label, and a `removed from MAM` value of
`ClientStatus` that `ClientStatusChecker` takes care not to overwrite — but nothing sets either.

One more problem with that existing piece: `ClientStatus` is qBittorrent's state for the torrent. The Link job
(`ClientStatuses.Update`) and **Check qBittorrent** write it on every run, and only one of those paths spares
`removed from MAM`. A mark stored there would be overwritten within minutes.

## Part 1: `mark_removed`

### Where it is recorded

A new nullable column `TorrentEntity.RemovedFromMamAt` (migration `AddRemovedFromMam`), set when MAM is found to no
longer have the torrent, and never cleared by qBittorrent status updates. `ClientStatus` goes back to meaning only
qBittorrent's state: the `ClientStatuses.RemovedFromMam` constant and the special case in `ClientStatusChecker` go.
The migration moves any existing `ClientStatus = 'removed from MAM'` into `RemovedFromMamAt` (set to the migration
time) and clears the status; there should be none, since nothing ever wrote it.

A second nullable column, `TorrentEntity.MamCheckedAt`, records when a torrent was last looked up for this purpose,
so one isn't looked up on every run (below).

### How it works

In `AutograbberRunner.RunAsync`, when `entry.MarkRemoved` is set, after the result pages have been read:

1. Collect the MAM ids of every result MAM returned on the pages read, before Fetchshelf's own filtering.
2. Take the lowest and highest of them. The candidates are stored torrents (`Torrents`) with a `MamId` in that range
   that MAM didn't return, that aren't marked removed already, and whose `MamCheckedAt` is unset or older than 7
   days.
3. Look each candidate up by MAM id (`MamSearchQuery { Id = … }`, the lookup the Download job already uses), at most
   **20 per run**, oldest `MamCheckedAt` first. The autograb holds `MamAccessGate` for its whole run, and the client's
   search politeness spaces the requests.
4. Found: set `MamCheckedAt`. Not found: set `RemovedFromMamAt` and `MamCheckedAt`, append a `RemovedFromMam` event
   (the event's existing shape: torrent id, MAM id, time), and log
   `Autograb Bookmarks: MAM no longer has Book by Author (MAM id 123456); marked as removed from MAM`.
5. With `dry_run`, the lookups still happen (they only read), but nothing is written; what would be marked is logged.

Why the limits: a gap in the results usually isn't a removal. MAM's search narrows by category, language and the
query, so stored torrents that simply don't match the search fall inside the id range of every page. Without a
recheck interval and a cap, a block over a large library would look up the same present torrents on every run. 20
lookups at the search politeness delay add a few seconds to a run; a removal is found within a week of the torrent
falling in a searched range.

The whole check is skipped for a run that reads no results, and for `type = "bookmarks"`, whose results aren't a
contiguous id range (a bookmark list skips everything not bookmarked), so every stored torrent would look like a
gap. Setting `mark_removed` on a bookmarks block is a startup error saying so.

### Where it shows

- **Torrents page:** a *Removed from MAM* entry in the Problems filter (`TorrentListFilters`), and the label in the
  list next to the client status.
- **Torrent page:** a notice "MAM no longer has this torrent (found on 2026-11-12)." The **MAM** link stays, since
  MAM may show why.
- **Events page:** the existing `Removed from MAM` event.
- Nothing is done in qBittorrent or the library: the torrent keeps seeding and stays linked. Acting on it is the
  user's choice.

## Part 2: `on_invalid_torrent`

### When it applies

In the Link job, when looking an unknown torrent up on MAM finds it but its details can't be used:
`MamTorrentMapper.Map` throws `UnrecognizedReferenceDataException` (an unknown media type, category, language or
other reference value). Today the run's per-torrent `catch` records the exception's message as a Link error, and the
next run looks the torrent up again, and fails again, on every run.

`LinkRunner` catches that exception separately and, when the torrent's `[[qbittorrent]]` connection has
`on_invalid_torrent`:

- sets its category (creating the category in qBittorrent if needed, as `on_cleaned` does), and/or
- adds its tags,

through the same code `Cleaner.RetireAsync` uses for `on_cleaned` and `on_unlinked`, moved into a small shared
helper (`QBittorrentActions.ApplyAsync(client, hash, setting)`). The Link error is still recorded, with what was
done: `MAM's details for this torrent can't be read (unrecognized media type: '17'); applied on_invalid_torrent:
category invalid, tags fetchshelf-invalid.`

A category that no library uses takes the torrent out of the Link job's way: it no longer matches a library, so it
isn't looked up again on later runs. With only tags, it keeps being looked up on each run as today, and tagging it
again changes nothing.

Not covered: a torrent MAM doesn't have at all (*MAM has no torrent with this info-hash*). That is usually a torrent
from another tracker, which already gets its own once-a-day retry, and isn't "invalid".

## Validation

- `mark_removed = true` on an autograb with `type = "bookmarks"`: `Invalid configuration: the [[autograb]]
  'Bookmarks' sets 'mark_removed', which needs a search by id range; bookmark lists aren't one.`
- `on_invalid_torrent` with neither `category` nor `tags`: rejected, as an empty table that does nothing. The same
  rule is added for `on_cleaned` and `on_unlinked`, which have the same shape and the same problem.

## Tests

- `AutograbberRunner` tests with a fake MAM: a stored torrent inside the result range and missing from the results
  is looked up; not found → `RemovedFromMamAt`, `MamCheckedAt` and an event; found → only `MamCheckedAt`; checked
  less than 7 days ago → not looked up; more than 20 candidates → 20 looked up, oldest first; outside the range or
  already marked → not looked up; `dry_run` → nothing written; `mark_removed = false` → no lookups.
- Migration test: an old `removed from MAM` client status moves to `RemovedFromMamAt`.
- `ClientStatusChecker` and Link job tests: a qBittorrent status update leaves `RemovedFromMamAt` alone.
- `LinkRunner` tests: an unrecognized media type with `on_invalid_torrent` sets the category and tags and records an
  error naming them; without the setting, the error as today; a torrent not on MAM is not touched.
- `Cleaner` tests keep passing with the shared helper.
- Config tests: `mark_removed` on bookmarks, and empty `on_invalid_torrent`/`on_cleaned`/`on_unlinked` tables, are
  rejected.
- Web tests: the Torrents filter and the torrent page notice.

## Documentation

`docs/configuration.md`:
- `mark_removed`: what it does, the 20-per-run and 7-day limits, not for `bookmarks`, and that nothing is changed in
  qBittorrent or the library. "Nothing acts on it yet" goes.
- `on_invalid_torrent`: when it applies (MAM's details have a value Fetchshelf doesn't know, such as a new media
  type), what it does, and why a category is the useful choice. "Nothing acts on it yet" goes.

`docs/user-guide.md`: the Torrents page's Problems filter and the torrent page notice.

## Release notes

Under **Added**: `mark_removed` and `on_invalid_torrent` now work. Under **Upgrading**: a database migration adds two
columns. `mark_removed = true` on a `bookmarks` autograb, or an empty `on_invalid_torrent`, `on_cleaned` or
`on_unlinked` table, is now refused at startup. Written at release time.

## Out of scope

- Acting on removed torrents (changing their category or tags, unlinking them).
- Finding removed torrents other than through autograb result ranges, for example by checking every stored torrent
  now and then.
