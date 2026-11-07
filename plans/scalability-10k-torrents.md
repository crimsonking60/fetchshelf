# Scaling to 10,000 torrents — implementation plan

## Context

Fetchshelf runs comfortably with about a hundred torrents. A single qBittorrent instance can seed around 10,000,
so that is the target here: 10k tracked torrents in one qBittorrent, with the Link job on its default 10-minute
interval, and pages that stay responsive.

The database is not what limits this. SQLite handles row counts far beyond 10k, and the tables are narrow and
indexed where lookups happen. The limits are in how the code uses the data:

| Area | Where | Why it grows badly |
|---|---|---|
| Torrents page | `Pages/Torrents/Index.cshtml.cs` `LoadAsync` | Loads every row, deserializes every `MetadataJson`, then filters, sorts and pages in memory, on every view. |
| Events page | `Pages/Events/Index.cshtml.cs` | Loads every matching event and sorts in memory. Events only grow. |
| Link run | `LinkRunner.RunCoreAsync` | One `DbContext` for the whole run, one `SaveChangesAsync` per torrent. Each save scans every entity tracked so far: about n²/2 entity scans per run. |
| Link run | `LinkRunner.SyncMismatch` | Recomputes the folder name for every already-linked torrent on every run, with `Directory.Exists` probes. Slow on network storage. |
| Clean job | `DuplicateCopies.GroupBySameBook` | Compares each copy with the first copy of every group: about n²/2 comparisons, each normalizing two titles. |
| MAM requests | `LinkRunner.ProcessFinishedTorrentAsync` | A torrent whose info-hash lookup fails gets no row, so it is looked up again on every run. A large existing client means one serial lookup per torrent on the first run. |
| qBittorrent | `LinkRunner.FindInQBittorrentAsync` | Fetches the client's whole torrent list per relink, so a bulk relink of N torrents downloads it N times. |
| SQLite | `Program.cs` | No WAL, so the default rollback journal applies: a long job write blocks page reads. |

Rough expectations today (estimates, not measurements): fine up to about 1k; the Torrents and Events pages and
the Clean job get slow at 1–3k; at 5–10k the Link run takes minutes, can outlast its interval, and page views
take seconds.

Out of scope: replacing SQLite, running more than one instance, background workers in a separate process, and
the auto-downloader's per-candidate checks, which are already indexed lookups and scale with search results,
not library size.

## Approach

Measure first, then fix one hot spot at a time, each change measured against the same seeded data set. Each step
is a commit of its own on `develop` and must keep `dotnet build`, `dotnet test` and
`dotnet format --verify-no-changes` clean. Behavior must not change: the existing tests for each area are the
guard, and new tests cover the new code paths.

## Step 0 — Benchmark data and timing tests

Before changing anything, make the problem measurable.

Done. What was built:

- **Seeder:** `tests/Shared/Scale/ScaleSeeder.cs`, compiled into both `Fetchshelf.Data.Tests` and
  `Fetchshelf.Web.Tests`. It builds N torrent rows of a library in steady state, the same data for the same N:
  - 60% audiobooks and 40% ebooks, 85% English, with shared authors, narrators and series;
  - every 20th book has a lesser copy already retired by the Clean job (about 5% of rows);
  - a Grabbed and a Linked event per book, Updated events for 10%, and a Cleaned event per retired copy, about
    two events per torrent in all;
  - 1% extra finished torrents in a library's category whose MAM lookup failed, recorded as linker errors;
  - 10% extra torrents in a category no library takes.
- **Fake qBittorrent:** the existing `OrganizerTestSupport.CreateQBittorrentClient`, serving the seeded list.
- **Timing tests:** `[ScaleFact]` tests in the `Scale` category. They are skipped unless `FETCHSHELF_SCALE`
  holds the number of torrents to seed, so `dotnet test` and CI skip them without a filter:

  ```
  FETCHSHELF_SCALE=10000 dotnet test --filter "Category=Scale" --logger "console;verbosity=detailed"
  ```

  - `OrganizerScaleTests`: a Link run with everything already linked, and a Clean run with no new duplicates.
    Both check the outcome (nothing linked or retired, one failed lookup per linker error) as well as the time.
  - `PageScaleTests`: the Torrents page with no filter, a text filter and the last page, and the Events page
    with and without a type filter. Each request is warmed up once and then averaged over three.

  The tests log their timings but don't assert time limits yet. Add generous limits once the fixes have landed.
- The seeded libraries' folders don't exist on disk, so the `Directory.Exists` probes in the Link run are as
  cheap as they get. On network storage the Link run will be slower than measured here.

## Step 1 — SQLite WAL mode

Done, as an opt-in. A top-level `database_wal` setting, default `false`, makes `DatabaseMigrator.Migrate`
switch the database to WAL after migrating; off by default so a database on a network share keeps working after
an upgrade. Turning it off again switches a database back to SQLite's default journal mode.
`docs/configuration.md` explains when to turn it on and to leave the `-wal` and `-shm` files in place.

- At startup, after migrations, run `PRAGMA journal_mode=WAL;` (it persists in the database file once set).
- Keep `synchronous` at its default (`FULL`). `NORMAL` is safe with WAL too, but needs no decision at this
  volume.
- The Docker volume (`/database`) is a local bind mount, so WAL's shared-memory file works. Add a line to
  `docs/configuration.md` noting the database must not be on a network share, which was already unsafe for
  SQLite.
- Test: the startup migrator test asserts the journal mode is `wal`.

## Step 2 — Clean job: bucket by title key first

Done, a little differently from the original plan below. `GroupBySameBook` normalizes each copy's title once
and compares a copy only with the groups of the same normalized title. Bucketing on the stored
`NormalizedTitleKey` was dropped: it would have needed every row's key to be current, and normalizing in
memory is just as fast. Groups and their order are unchanged, and two new tests cover interleaved titles and
titles that differ only in case and article. The Clean run at 10k went from 84 s to 0.6 s.

Original plan:

`AreCopiesOfSameBook` already requires equal normalized titles, and `TorrentEntity.NormalizedTitleKey` stores
exactly that value.

- Add an overload of `DuplicateCopies.GroupBySameBook` that takes a title-key selector, groups the copies by that
  key, then runs the existing first-of-group comparison inside each bucket. The order of copies and groups is
  preserved, so the keeper choice and grouping results stay the same.
- `Cleaner` passes `t => t.NormalizedTitleKey`.
- Also normalize each title once per call instead of once per comparison.
- Tests (`DuplicateCopiesTests`):
  - the bucketed grouping gives the same groups as the current one on the existing fixtures;
  - a new case with two different books sharing a title key (different authors) stays two groups.
- Open question to check while implementing: `NormalizedTitleKey` must be current for every row. Refresh and
  manual overrides already update it; `TitleMaintenanceRunner` covers older rows. If any path can leave it stale,
  bucket on `TitleNormalizer.Normalize(metadata.Title)` instead, which still makes the grouping near-linear.

## Step 3 — Link run: keep the change tracker small

Done, more simply than planned, plus batched commits the plan didn't foresee:

- **Change tracker:** the run clears it after each torrent. Every path through a torrent already saves what it
  changed, so nothing is lost. That alone took the 10k Link run from 145 s to 4 s; the up-front loading of
  torrents and error rows below wasn't needed.
- **Commits:** most torrents only get a status update, and seeding torrents flip between `uploading` and
  `stalledUP` all the time. Each update committed on its own costs a disk sync. On a real disk, 3,334 changed
  statuses took 34 s, and a NAS hard disk would be slower still. The run now processes torrents inside a
  transaction committed every 100 torrents. It is also committed:
  - before every MAM lookup, because a lookup can wait on the MAM gate while another job needs to write, and
    holding the write lock then would block that job;
  - right after a torrent's files are placed, so a crash can't leave files without a record;
  - when the run ends, including when it ends with an exception.
- **New tests:** a run over more than one batch saves every status; a run stopped by MAM rate limiting keeps the
  statuses written before it; another connection can write to the database while a lookup is in progress. That
  last test fails within a second if the pre-lookup commit is removed. A new scale test changes a third of the
  statuses.

Original plan:

- Load the run's existing torrents up front, in one query, into a dictionary keyed by hash, for the hashes the
  client reports as finished. This replaces one `FirstOrDefaultAsync` per torrent.
- Load the run's linker error rows the same way, so `ErrorLog.ClearAsync` doesn't query once per torrent. Keep
  `ErrorLog`'s API and let it find them in `Local`, which it already checks first.
- Save in batches: track pending changes and call `SaveChangesAsync` every 100 torrents and at the end, instead
  of after each one.
  - Linking itself, which places files, still saves right after the files are placed. A crash must not leave
    placed files with no record of them, so that save stays per torrent.
  - Only the read-and-sync path ("already linked, update status and mismatch") is batched.
- When a torrent's processing throws, the current `ChangeTracker.Clear()` would discard the unsaved batch. Save
  the batch before processing each torrent that will link files, and on an error clear and reload only what's
  needed, so a failure never loses another torrent's state.
- `EventStore.AppendAsync` saves on every call. Leave that as it is: it only happens when something was linked
  or changed, which is rare per run.
- Tests (`LinkRunnerTests`):
  - existing behavior tests keep passing;
  - a new test where one torrent in the middle of a batch throws, and the torrents before and after it still
    get their status and mismatch saved.

## Step 4 — Link run: only recompute mismatch when inputs changed

`SyncMismatch` recomputes the folder for every linked torrent on every run. The inputs are the torrent's
category, save path, tags, its stored metadata, the library config and `exclude_narrator_in_library_dir`.

- Store a fingerprint of those inputs on the torrent row: a new nullable column `MismatchInputsHash`, a short
  hash of category, mapped save path, sorted tags, `MetadataJson`, the matched library's name and dir, and the
  narrator setting. This is the one migration in this plan.
- Recompute and store the mismatch state only when the fingerprint differs; otherwise leave it as it is.
- A config change alters the fingerprint for every row, so the first run after a change recomputes everything,
  as it does today.
- `Directory.Exists` probes inside `FolderNameComputer` check whether the base folder name is taken. That can
  change without any input changing, when another book gets linked into the same name. Check whether
  `FolderNameComputer` uses the probe only to pick a free name for new links. If it also affects the mismatch
  result, keep the probe for torrents whose stored folder isn't the base name, and document the rule in the code.
- Tests:
  - the mismatch state is unchanged when the fingerprint matches;
  - it is recomputed after a category change, a metadata refresh, or a library config change.

## Step 5 — MAM: back off on failed lookups

- A failed info-hash lookup already writes a linker `ErroredTorrents` row keyed by hash, and a repeat failure
  updates its `OccurredAt`. Use that as the backoff record: before looking up an untracked torrent, skip it if it
  has a linker error with the lookup-failed text newer than the backoff period.
  - To avoid matching on text, add an error kind, either a new `ErrorStage` value (`LinkerLookup`) or a small
    column on the error row. Prefer the new stage: it needs no migration, and the Errors page groups by stage.
- Backoff: a fixed 24 hours. No new setting; add one later only if someone needs it.
- The Errors page's Restore and Remove actions clear the row, so an immediate retry is still one click away.
  Check that both paths work for the new stage.
- Onboarding a large existing client: limit the Link run to a number of new MAM lookups per run (e.g. 200) and
  log how many are left for later runs. Already-tracked torrents are unaffected, so an existing library keeps
  linking normally while the backlog drains.
- Tests (`LinkRunnerTests`, with the fake metadata lookup):
  - a torrent whose lookup failed is not looked up again within the backoff period, and is after it;
  - the per-run cap leaves the rest for the next run and links them then.
- Document both in `docs/user-guide.md` under the Link job.

## Step 6 — qBittorrent: look up single torrents by hash

Done, and wider than planned. Besides relinking, the torrent detail page (three lookups per view) and the
Download job's "does qBittorrent already have it" check (one per added torrent) also fetched the whole list to
find one torrent. All of them now use `QBittorrentClient.GetTorrentAsync`. The full list is fetched only where
everything is needed: the Link run, the Check client action and the Search page's presence cache.

Original plan:

- `LinkRunner.FindInQBittorrentAsync` uses `torrents/info?hashes=<hash>` per connection instead of listing
  everything. Add `QBittorrentClient.GetTorrentAsync(hash)` next to `HashExistsAsync`, and let `HashExistsAsync`
  build on it.
- Tests: the client test for the new call, and the existing relink tests.
- Not now: switching the Link run to `/api/v2/sync/maindata` for incremental diffs. With Step 3, one full
  `torrents/info` read every 10 minutes is cheap enough at 10k. Revisit only if measurements say otherwise.

## Step 7 — Torrents and Events pages: query in SQL

This is the largest step. The page comment names three blockers; each gets a fix.

1. **Ordering by `DateTimeOffset`:** add a value converter so `CreatedAt` (Torrents) and `OccurredAt` (Events)
   are stored as sortable numbers, using EF's `DateTimeOffsetToBinaryConverter` or a UTC-ticks `long`.
   - This needs a migration that rewrites the existing text values.
   - Apply it to all `DateTimeOffset` columns at once, through `ConfigureConventions`, so the rule is uniform.
   - Check the migration's data conversion against a copy of a real `data.db` before committing.
2. **Fields inside `MetadataJson`:** promote what the page filters on to columns kept in sync wherever
   `MetadataJson` is written (link, refresh, manual override, title maintenance):
   - `Language` and `MediaType`, both indexed;
   - `SearchText`: a lowercased, normalized concatenation of title, authors, series and narrators, i.e. whatever
     `TorrentSearchMatcher` matches on today.

   Put the write in one helper (e.g. `TorrentEntity.SetMetadata(TorrentMetadata)`) so no path can forget a
   column. The migration backfills them from `MetadataJson`. `TitleMaintenanceRunner`, which already rewrites
   rows, can backfill instead, if a C# backfill is simpler than SQL.
3. **Case-sensitive substring match:** search `SearchText` with the query lowercased the same way.
   - Check `TorrentSearchMatcher` first. If it does more than substring matching (word splitting, diacritics),
     either mirror that in how `SearchText` is built, or keep the SQL query as a pre-filter and run
     `TorrentSearchMatcher` on the much smaller result.
   - The result must be the same as today's.

Then:

- `LoadAsync` builds an `IQueryable` from the filters, counts in SQL, sorts and `Skip`/`Take`s in SQL, and
  deserializes `MetadataJson` only for the rows on the page.
- `TorrentListFilters.MatchesStatus` and `MatchesProblem` need SQL-translatable equivalents. Move them to
  expression-returning methods so the Core-layer tests can still exercise them against in-memory lists.
- Dropdown options (libraries, categories, languages, media types) come from `SELECT DISTINCT` queries, not
  from the loaded rows.
- The Events page gets the same treatment, now that `OccurredAt` is sortable. Its field filter
  (`FieldChangesJson.Contains`) already translates to SQL.
- Remove the "acceptable trade-off" comment from `LoadAsync`.
- Tests:
  - the existing Torrents page and filter tests keep passing;
  - new tests seed a few dozen torrents and check that every filter, both sort orders and the last page give the
    same rows as before. Run them against SQLite, not the in-memory provider, so SQL translation is actually
    exercised.

## Step 8 — Smaller full-table loads

`ClientStatusChecker`, `RetroactiveTagApplier` and `TitleMaintenanceRunner` load the whole Torrents table. They
are on-demand or rare, and one pass over 10k rows is fine, so leave them. But give them the Step 3 treatment
where they save inside a loop: one `SaveChangesAsync` at the end, or in batches.

## Results

From the Step 0 tests, on a development machine with the database on local disk. Add a row set after each step.

**Baseline (before any fix), 2026-11-07:**

| Test | 1k | 5k | 10k |
|---|---|---|---|
| Link run (all linked, nothing new) | 4.5 s | 38 s | 145 s |
| Clean run (no new duplicates) | 1.4 s | 21 s | 84 s |
| Torrents page, no filter | 42 ms | 159 ms | 286 ms |
| Torrents page, text filter | 33 ms | 135 ms | 219 ms |
| Torrents page, last page | 35 ms | 130 ms | 188 ms |
| Events page, no filter | 17 ms | 54 ms | 97 ms |
| Events page, type filter | 8 ms | 24 ms | 48 ms |
| Events seeded | 2,056 | 10,223 | 20,477 |

What the baseline shows:

- **The Link and Clean jobs are quadratic, as expected.** Going from 1k to 10k makes them about 32× and 60×
  slower. At 10k a Link run with nothing to do takes almost 2.5 minutes, every 10 minutes, before any
  network-storage cost. Steps 2, 3 and 4 matter most.
- **The pages grow linearly and are still fast at 10k.** Even loading everything, a page takes under 0.3 s. The
  estimate that they would struggle first was wrong. Step 7 drops in priority: worth doing for 50k and beyond,
  not needed for 10k.
- **MAM lookups:** one per failed lookup on every run (100 at 10k), as expected. Step 5 stays.

**After steps 1, 2 and 6, at 10k:**

| Test | Baseline | Now |
|---|---|---|
| Link run (all linked, nothing new) | 145 s | 152 s |
| Clean run (no new duplicates) | 84 s | 0.2 s |
| Torrents page, no filter | 286 ms | 280 ms |
| Events page, no filter | 97 ms | 95 ms |

The Clean job is fixed. The Link run is unchanged within noise: WAL doesn't help it, because its cost is the
change tracker (Step 3), not the database. The single-torrent lookups (Step 6) aren't covered by these tests.

**Measuring on a real disk:** every timing above ran with the database in `/tmp`, which on the development
machine is a RAM-backed tmpfs where disk syncs cost nothing. That hid the cost of committing each torrent on its
own. From Step 3 on, the organizer timings are taken with `TMPDIR` pointing at the home disk (ext4):

```
TMPDIR=$HOME/.cache/fetchshelf-scale FETCHSHELF_SCALE=10000 dotnet test --filter "Category=Scale" --logger "console;verbosity=detailed"
```

**After Step 3, at 10k, database on ext4:**

| Test | Before Step 3 | After Step 3 |
|---|---|---|
| Link run (all linked, nothing new) | 145 s (tmpfs) | 4.3 s |
| Link run, a third of statuses changed | 34 s (change tracker cleared, commits not batched) | 4.3 s |
| Clean run (no new duplicates) | 0.2 s | 0.2 s |

**Same tests with the database on a spinning hard disk** (ext4 on a 14.6 TB HDD), the kind of disk a NAS uses.
"Unbatched" is the same code with a batch size of 1, i.e. one commit per changed torrent:

| Test | Unbatched | Batched (100) |
|---|---|---|
| Link run (all linked, nothing new) | 7.7 s | 7.2 s |
| Link run, a third of statuses changed | 124 s | 10.7 s |
| Clean run (no new duplicates) | | 0.2 s |

A commit costs about 36 ms on the hard disk, against about 1 ms on NVMe, so batching matters most there. The
nothing-changed run is slower on the hard disk than on NVMe because of the 100 failed MAM lookups. Each one
writes its error row in a commit of its own, about 3 s in all, and Step 5's backoff removes most of them.

The Link run now grows linearly, at about 0.4 ms per torrent. Its folder checks hit folders that don't exist
here, so network storage will add to it; Step 4 is about that.

## Order and size

Steps 1, 2, 5 and 6 are small and independent; do them first. Step 3 comes before Step 4. Step 7 is the largest
and can land last. It only affects page speed, not correctness or MAM safety, and the baseline shows the pages
are already fast enough at 10k, so it can also wait until a larger target calls for it.

## Docs to update

- `docs/user-guide.md`: Link job lookup backoff and per-run lookup cap (Step 5).
- `docs/configuration.md`: database location must be a local disk (Step 1).
- `CLAUDE.md`: under Persistence, note WAL mode, the promoted metadata columns, and that they must be written
  through the single helper.
