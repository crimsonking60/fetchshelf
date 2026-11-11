# Series completion and author follow — implementation plan

Depends on `plans/queryable-authors-and-series.md`: the series and author rows, and `LibraryQueries`, must be in
place first. It also uses `MamSortOrder.NewestByDate` from `plans/search-page.md`; if that plan isn't done yet,
this one adds the value.

## Context

Fetchshelf downloads what a rule in the config file asks for: an autograb's search, a snatchlist, a Hardcover
list. Nothing looks at the library itself and asks what is missing from it. A series with books 1–4 and 6 linked
stays without book 5 unless a search happens to find it, and a new book by an author the library is full of is
found only if an autograb's query covers that author.

The pieces exist:

- `[[autograb]]` blocks already search by series or author (`search_in = ["series"]`), map the results and hand
  each one to `SelectionRunner`, which drops what the library already has (`SameBookComparer`), prefers the
  better format and applies the cost.
- `[[hardcover_list]]` already searches MAM once per book, with `grab` blocks for filter and cost, a per-book
  status (`ListItemEntity`), a "search again after a day" rule and its own page.
- After `plans/queryable-authors-and-series.md`, the owned entries of a series and the books of an author are
  queries, not a scan of every row.

What's missing is a list of series and authors the user has chosen to follow, a job that searches for them, and a
page to choose them on.

## Change

### 1. What is followed

A `Follows` table, managed from the web UI (not the config file: following is a per-series choice made while
looking at the library, and there may be hundreds):

- `Id`, `Kind` (`Series` or `Author`), `NameKey` (the key from the series or people rows), `DisplayName`.
- `Mode`:
  - `Download`: what is found is selected automatically, through the normal selection rules and budgets;
  - `Watch`: what is found is listed on the Following page with a Download button, and nothing is selected;
  - `Ignore`: series only; hides the series from the "missing books" view, and is never searched.
- `CreatedAt`, `LastSearchedAt`, `LastFoundAt`.

Defaults when following: `Download` for a series (the books are the next ones in something already being read),
`Watch` for an author (a new author's book is a weaker guess). Either can be changed on the page.

A `FollowFinds` table records what a search found, one row per follow and MAM torrent: `FollowId`, `MamId`,
`Title`, `SeriesDisplay` (e.g. "5"), `MediaSide`, `Status` (`New`, `Selected`, `Wanted` (the cost was not
allowed), `Owned`, `Dismissed`), `FoundAt`, `StatusAt`. A torrent already in the library or the selection queue
is not recorded as a find at all.

### 2. Configuration

```toml
[follow]
search_interval = 60          # minutes between runs of the Follow job; 0 = run it from the Jobs page only
searches_per_run = 10         # MAM searches per run at most
search_again_after = 24       # hours before the same series or author is searched again
prefer_format = "audio"       # optional, as in [[hardcover_list]]
category = "audiobooks"       # optional, as in [[hardcover_list]]
dry_run = false

[[follow.grab]]
cost = "free"
categories = { audio = true, ebook = false }
```

- `grab` blocks are the same as a `[[hardcover_list]]`'s: a cost plus the shared filter, tried in order. With
  none, nothing is ever selected or listed as wanted; `ConfigLoader` rejects a `[follow]` without one.
- Languages come from the top-level `languages`, as for the Search page.
- Without a `[follow]` table there is no Follow job, and the follow buttons don't show.
- `docs/configuration.md` documents the table; `--check-config` checks it like the other blocks.

The request budget: `searches_per_run` searches per run, each follow at most once per `search_again_after`.
With the defaults that is at most 240 searches a day, and a library with 50 followed series searches each one
once a day. The search reads one page of 100 results; a series or author with more than 100 torrents on MAM is
searched newest first, so new books are still seen.

### 3. The Follow job

`FollowRunner` in `Fetchshelf.Data.AutoDownloader`, registered as the `Follow` job (`JobNames.Follow`, added to
`JobNames.Reserved` so no configured block can take the name). Its shape follows
`HardcoverListImporter`:

1. Take `MamAccessGate` for the whole run, read the account and work out the selection budget with `unsat_buffer`.
   A budget of 0 searches nothing, as the list importer does.
2. Pick the follows that are due (`Mode` not `Ignore`, `LastSearchedAt` older than `search_again_after`), the
   longest-waiting first, up to `searches_per_run`.
3. Per follow, search MAM:
   - a series: `SearchFields = [Series]`, the display name as text;
   - an author: `SearchFields = [Author]`;
   - both newest first by date, the top-level languages, and the main categories the `grab` blocks allow.
4. Map each result and keep only real matches, since MAM's search is a word match: for a series, a result whose
   series rows' key equals the followed key; for an author, one whose author keys contain it. The match uses the
   same `TorrentProjection` the series and people rows are made with, so it agrees with the library.
5. Drop what is owned or queued: anything `SelectionRunner` reports as a duplicate is not a find. For a series,
   entries already owned in the same media side (`LibraryQueries.OwnedEntriesAsync`) are dropped before that, so
   a second copy of book 3 isn't even considered.
6. For the rest, by entry number for a series (lowest first) and newest first for an author, the first `grab`
   block whose filter accepts it decides:
   - `Download` mode: `SelectionRunner.ProcessCandidateAsync` with that block's cost, the `category`, and
     grabber name `Follow: <display name>`. A `Selected` outcome is recorded as a `Selected` find; a `CostNotFree`
     one as `Wanted`.
   - `Watch` mode: recorded as a `New` find.
   - `prefer_format`: when both an audiobook and an ebook of the same entry are found, only the preferred one is
     taken, unless only the other has a copy a block's cost accepts; then that one is taken rather than nothing.
   - A series entry counts as owned only when owned entries cover every book it holds: an omnibus `3-5` is not
     owned when only book 3 is.
   - The followed name is searched as plain words (`ListMamQueryBuilder.PlainWords`), without anything MAM's
     search syntax would read as an operator.
7. Stop selecting at the run's budget; the remaining follows are searched next run. The follow it stopped in
   keeps its old `LastSearchedAt`, so it comes first next time. Watched follows select nothing, so they are
   still searched when the budget is spent.
8. Errors are handled as in the list importer: an account-wide MAM error (blocked, rate limited, forbidden) fails
   the run; any other error is recorded on the Errors page under a new `Follow` stage keyed by the follow, and
   the run moves on. A failed follow still counts as searched, so one that keeps failing doesn't take a search
   from the others on every run. Stopping a follow removes its error too.

`dry_run` runs everything and writes no selection, find or `LastSearchedAt`, logging what would have happened.

The job's outcome text: "12 followed searched, 3 selected, 2 new to look at". `JobOutcomes` gets a `Follow`
entry.

### 4. Gaps in a series

`SeriesGaps.Of(ownedEntries)` in `Fetchshelf.Core/Metadata/` works out the missing numbers between 1 and the
highest owned whole number: owned `1, 2, 3-4, 6` gives `5`. Ranges count as every number they cover; parts
(`2p1`) count as their book; fractional entries (`1.5`) are never reported missing. A series whose lowest owned
number is above 1 reports the numbers below it too ("1–2 missing").

This is shown on the Series page; the Follow job doesn't need it, as it searches the whole series anyway, and
also finds entries after the highest owned one.

### 5. Pages

**Series** (`Pages/Series/Index`): every series in the library from `LibraryQueries.SeriesAsync`.

- Columns: series, authors, owned (`1–4, 6`), missing (`5`), media types, follow state.
- Filters: "with missing books", "followed", "ignored", and a text search; sorted by name; paged like the
  Torrents page.
- Per row: Follow (download) / Watch / Ignore / Stop following, as htmx buttons that swap the row.

**Following** (`Pages/Following/Index`): every follow, with its finds.

- Each follow shows its mode, when it was last searched and its finds, new ones first.
- A `New` or `Wanted` find has Download and Download with wedge buttons (the Search page's handlers, through
  `SelectionRunner`) and Dismiss. Owned and selected finds are shown greyed out with their status.
- A "Search now" button per follow clears its `LastSearchedAt`, which makes it the first one due, and starts the
  Follow job. That keeps every MAM search inside the job, under its gate and request budget.
- The page's buttons are plain posts with a message after the redirect; the Series page's buttons swap their row
  with htmx.
- A Download click on a find asks MAM for the torrent again, under `MamAccessGate` (waiting up to 10 seconds for a
  running job), so the cost is today's. A `Wanted` find is shown as "Not free".
- The navigation gets "Following", with the count of new finds as a badge.

**Torrent detail page**: a "Follow" row with a "Follow <name>" button for each series and author, or a
"Following <name>" link to the Following page for one already followed. The Torrents page shows the same next to
its Author and Series filters.

**Series page authors**: `LibraryQueries.SeriesAuthorsAsync` gives each series' authors, the one with the most books
first; the page shows three and a count of the rest, as comics list many people.

## Tests

- `SeriesGapsTests` (Core): plain gaps, ranges, parts, fractions, a series starting above 1, nothing missing.
- `FollowRunnerTests` with a fake MAM handler and SQLite:
  - a series follow selects the missing entry and leaves the owned ones;
  - a result whose series only shares a word with the followed one is ignored;
  - an author follow in Watch mode records a `New` find and selects nothing;
  - the cost not allowed by any `grab` block records `Wanted`;
  - `prefer_format` with both formats found;
  - `searches_per_run` and `search_again_after` pick the right follows, longest-waiting first;
  - the selection budget stops selection and leaves the rest for the next run;
  - an account-wide MAM error fails the run; another error records a `Follow` error and continues;
  - dry run writes nothing.
- Config tests: `[follow]` without `grab` rejected; defaults; unknown keys rejected.
- Page tests: Follow / Watch / Ignore on the Series page change the row; Download from a find selects it;
  Dismiss hides it.

## Documentation

- `docs/user-guide.md`: a "Completing series and following authors" section after the Hardcover one: the Series
  page, the two modes, the Following page, the request budget and why it is there.
- `docs/configuration.md`: the `[follow]` table.
- `docs/config-examples/5-everything.toml`: a `[follow]` table (there is no `example-config.toml`).

## Release notes

- Added: the Series page shows every series in the library with its missing books, and a series can be
  followed so its missing and future books download automatically.
- Added: authors can be followed from a book's page; their new uploads are listed on the Following page, or
  downloaded automatically.
- Upgrading: nothing to do. Following needs a `[follow]` table in the config; without one, nothing changes.

## Out of scope

- Following a narrator.
- Series data from Audiobookshelf or Hardcover (both know series the library doesn't have yet).
- Following series or authors not in the library yet (from a search result): a later addition to the Search
  page.
- Notifications for new finds: a `follow_found` event when a run records new `Watch` finds is an easy follow-up.
