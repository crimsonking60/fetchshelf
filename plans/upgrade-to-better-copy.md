# Upgrade to a better copy — implementation plan

## Context

Fetchshelf already replaces a worse copy with a better one, but only by chance:

- `SelectionRunner.CheckDuplicateAgainstLibraryAsync` lets a candidate through when the library's copy of the same
  book ranks worse in the format preference (`audio_types`, `ebook_types`; lower `FormatRanker` rank is better).
- Once the better copy is downloaded and linked, the Clean job (`Cleaner.RunAsync`) groups the copies of one book,
  keeps the best (`DuplicateCopies.PickKeeper`, by rank then size) and retires the rest.

So an `mp3` copy becomes `m4b` only if some autograb, list or manual search happens to return the `m4b` torrent.
Nothing looks for one, nothing shows which books could be better, and an upgrade found by an autograb is paid
for at that autograb's `cost`, which may be `ratio` or `wedge` for a book that is already owned.

## Change

### 1. Which books could be better

A linked copy "could be upgraded" when its rank is above 0: its chosen format isn't the first one in its
preference list. The rank is the one the Clean job keeps by:

- `Cleaner.Rank` moves to a shared `LibraryCopyRank.Of(TorrentEntity, FormatPreferenceLists)` in
  `Fetchshelf.Data/Organizer/`, used by both the Clean job and this plan, so the two can't disagree.
- A copy with no chosen extension (metadata-only, or not linked) is never upgradable.
- A copy whose format isn't in the list at all (`NotPresent`) is upgradable.

### 2. The Torrents page

- A "Could be upgraded" choice in the Problem filter's dropdown, listing linked, not replaced copies with a rank
  above 0. "Any problem" leaves it out: a worse format is no problem. Rows that could be upgraded show the chosen
  format and the best one in the list ("mp3; m4b preferred"), whatever the filter.
- The torrent detail page shows the same line, and a "Look for a better copy" link: the Search page with the
  title and first author as search text. The Search page looks in titles and authors by default; its media side
  is a browser-side filter, so the link doesn't set one.

### 3. The Upgrade job

Optional and off unless configured:

```toml
[upgrade]
search_interval = 360        # minutes between runs; 0 = run it from the Jobs page only
searches_per_run = 20        # MAM searches per run at most
search_again_after = 30      # days before the same book is searched again
libraries = ["Audiobooks"]   # optional: only copies in these libraries; default every library
dry_run = false

[[upgrade.grab]]
cost = "free"                # the default, and the reason for this block: upgrades should cost nothing
```

`grab` blocks work as in `[[hardcover_list]]`: a cost plus the shared filter, tried in order. A `[upgrade]`
without any is given one `cost = "free"` block with no filter, so the safe choice is the default.

`UpgradeRunner` in `Fetchshelf.Data.AutoDownloader`, registered as the `Upgrade` job (`JobNames.Upgrade`, added to
`JobNames.Reserved`):

1. Take `MamAccessGate` for the run, read the account, work out the selection budget with `unsat_buffer`.
2. Pick upgradable copies (section 1) in the configured libraries whose `UpgradeSearchedAt` (a new nullable
   column on `TorrentEntity`) is empty or older than `search_again_after`, never-searched first, then oldest
   first, up to `searches_per_run`.
3. Per copy, search MAM for the title with the first author, `SearchFields = [Title, Author]`, in the copy's
   language and main category (audiobooks or ebooks).
4. Keep results that are the same book and rank better than the copy: the same title key and then
   `SameBookComparer`, as selection judges duplicates (the comparer leaves titles out). Take the best ranked,
   then the most seeders. The search is limited to the audiobook or ebook main category for those media types,
   and not limited for music and radio.
5. The first `grab` block whose filter accepts it decides the cost; hand it to
   `SelectionRunner.ProcessCandidateAsync` with:
   - the copy's own qBittorrent category as `autograbCategory`, when it is a library's category, so the better copy
     lands in the same library as the one it replaces; otherwise `null`, so the tag rules decide;
   - grabber name `Upgrade`.
6. Set `UpgradeSearchedAt` whatever the outcome, so a book with no better copy waits `search_again_after` days.
7. The Clean job retires the old copy once the new one is linked, as it does today. Nothing else changes there.

Errors as in the list importer: account-wide MAM errors fail the run, other errors are recorded on the Errors page
under a new `Upgrade` stage keyed by the torrent, and the run moves on.

The outcome text: "20 books searched, 3 better copies selected".

### 4. Upgrades found by other searches

These keep working as today. The only change is that the log line and the selection's grabber say it was an
upgrade: `SelectionOutcome` gains `SelectedAsUpgrade`, returned instead of `Selected` when a library copy of the
same book exists and ranks worse. Callers that count selections treat both alike.

## Tests

- `LibraryCopyRankTests`: audio and ebook copies, a format not in the list, a copy with nothing chosen; the Clean
  job's tests keep passing on the moved code.
- Torrents page: "Could be upgraded" lists the right rows and leaves out replaced and best-format copies.
- `UpgradeRunnerTests` with a fake MAM handler and SQLite:
  - a better copy is selected into the old copy's library category;
  - a result of the same rank, a worse one, or a different book is not;
  - the cost check: a result that is not free is not selected with the default `grab` block;
  - `searches_per_run`, `search_again_after` and `libraries` pick the right copies;
  - `UpgradeSearchedAt` is set when nothing better is found;
  - the selection budget stops the run's selections;
  - dry run writes nothing.
- `SelectionRunnerTests`: `SelectedAsUpgrade` when a worse library copy exists, `Selected` otherwise.
- Config tests: `[upgrade]` defaults, the default `grab` block, unknown library names rejected.

## Documentation

- `docs/user-guide.md`, "Better formats replace worse ones": the Upgrade job, the "Could be upgraded" filter and
  the "Look for a better copy" link; that upgrades are free unless a `grab` block says otherwise.
- `docs/configuration.md`: the `[upgrade]` table.
- `docs/config-examples/5-everything.toml`: an `[upgrade]` table (there is no `example-config.toml`).
- The Config page shows the `[upgrade]` settings, as it does `[follow]`.

## Release notes

- Added: the Upgrade job looks for better formats of books already in the library, such as `m4b` for an `mp3`
  copy, and selects them when they are free. The Clean job retires the old copy as before.
- Added: a "Could be upgraded" filter on the Torrents page, and "Look for a better copy" on a book's page.
- Upgrading: nothing to do. The Upgrade job needs an `[upgrade]` table in the config; without one, nothing changes.

## Changes after review

- Selection counts a candidate as an upgrade only against a copy in the library (linked, not replaced, not
  unlinked), not a metadata-only record or a copy still waiting.
- The Upgrade job leaves out torrents already known, queued or ignored, and tries the next better copy when
  selection turns one down; it stops at a duplicate of the library or the queue.
- It searches with `ListMamQueryBuilder.Build`, as the Hardcover lists do, and reads only the deciding columns of
  the library before loading the chosen copies whole.
- Errors of copies no longer searched (replaced, unlinked, or in the best format by now) are cleared each run.
- The "Look for a better copy" link searches in the book's language, as plain words.

## Out of scope

- A per-autograb `upgrades = false` to stop autograbs paying for upgrades they happen to find.
- Upgrading by something other than format (bitrate, an abridged copy, a better narrator).
- (Done after review, no longer out of scope:) a library's own `audio_types`/`ebook_types`.
  `FormatPreferenceLists.Libraries` carries them, so a copy is ranked by the list the Link job chose its format
  from, by the Clean job, selection, the Torrents page and the Upgrade job alike. Selection also ranks a linked
  library copy by its chosen format rather than its MAM listing, so the two agree on what is better.
