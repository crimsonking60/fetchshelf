# Follow the series that are in Audiobookshelf — implementation plan

## Context

The Follow job (`[follow]`, PR #49) searches MAM for followed series and authors and lists or selects what is
missing. Series and authors are followed from Fetchshelf's own pages, so only books Fetchshelf knows about are
starting points: a library built before Fetchshelf, or with other tools, has series Fetchshelf has never seen, and
the Series page doesn't show them.

Audiobookshelf knows every series in its libraries, whoever put the books there. Fetchshelf already talks to it
(`AbsClient`: libraries, search, author items, metadata updates) when `[audiobookshelf]` is configured.

The review's 5.10 proposed Audiobookshelf series "with missing entries" as a list source. A list needs a title per
wanted book; Audiobookshelf only knows the entries it has, so it can't say what the missing ones are called. The
Follow job already answers that, by searching MAM for the series. So the useful step is to follow those series, not
to list them.

## Change

### 1. Reading Audiobookshelf's series

- `AbsClient.ListSeriesAsync(libraryId)`: `GET /api/libraries/{id}/series` with paging (`limit`, `page`), returning
  each series' name and its books' sequence numbers. **Check first** the answer's shape against the Audiobookshelf
  version in use (the `/api/libraries/{id}/series` endpoint and its `books[].sequence` field) and record an example
  answer for the tests.
- Only book libraries (`mediaType = "book"`); podcast libraries are skipped.

### 2. Settings

In `[follow]`:

| Setting | Default | Description |
|---|---|---|
| `audiobookshelf_series` | `"off"` | `"off"`, `"watch"` or `"follow"`: the mode new follows get for Audiobookshelf's series. |
| `audiobookshelf_libraries` | `[]` | Audiobookshelf library names to read; empty means all book libraries. |

Needs `[audiobookshelf]`; refused at load without it.

### 3. Syncing

At the start of each Follow run (before taking the MAM gate, since this is Audiobookshelf only):

- Read the series of the chosen libraries.
- For each series not yet followed in any mode (by the follow name key, as the Series page uses), create a
  `FollowEntity` of kind series with the configured mode. A series the user set to *ignore* stays ignored; one already
  followed or watched keeps its mode.
- Series that left Audiobookshelf are not unfollowed: the follow is the user's from then on.
- The run's summary adds "3 series added from Audiobookshelf".

Searching then works as for any follow, within `searches_per_run` and `search_again_after`, so adding 400 series at
once spreads their searches over many runs rather than sending 400 at once.

### 4. Pages

- The Following page shows *from Audiobookshelf* on follows created this way (`FollowEntity.Source`, migration).
- The Series page lists Audiobookshelf series Fetchshelf has no books of, with the entries Audiobookshelf has.

## Tests

- `AbsClientTests`: series parsing and paging from the recorded answer.
- `FollowRunnerTests`: new series become follows in the configured mode; ignored and existing follows are left as
  they are; series that leave Audiobookshelf stay followed; an Audiobookshelf failure fails the sync, not the
  searches (logged, and the run goes on with the existing follows).
- Config tests: the setting without `[audiobookshelf]`; unknown library names.

## Documentation

- `docs/configuration.md`, `[follow]`: the two settings.
- `docs/user-guide.md`, Following and Series pages: series from Audiobookshelf.

## Release notes

- Added: `[follow]` can follow or watch every series in Audiobookshelf (`audiobookshelf_series`), so series from
  before Fetchshelf are completed too.
- Upgrading: nothing to do; off unless set.

## Out of scope

- Authors from Audiobookshelf: an author follow searches every upload by that author, which for a whole library
  is too many searches; series are the bounded case.
- A list source of Audiobookshelf books (there is no title for a missing entry to search for).

## As built

- **The check against Audiobookshelf.** Done against Audiobookshelf 2.37.2 and its server source. A series' books
  carry their place only in `media.metadata.seriesName`, as `"Saga #1"`, or `"A #1, B #2"` for a book in several
  series. There is no `books[].sequence` or `series` object any more. `AbsSeriesSequence.Of` finds a series' entry by
  its whole name, since names can hold `", "` (`"As You Wake, Break the Shell #1"`). Each book's formats come from
  `numTracks` and `ebookFormat`. The series list is read 100 a page, sorted by name.
- **Settings use the page's names.** `audiobookshelf_series` takes `"off"`, `"follow"` or `"auto"`, matching *Followed*
  and *Auto* on the pages, rather than the plan's `"watch"`/`"follow"`. `audiobookshelf_libraries` without
  `audiobookshelf_series` is refused. An unknown library name can only be found by asking Audiobookshelf, so it is
  logged at sync time rather than refused at load.
- **A gap in the plan: Audiobookshelf's books count as owned.** Followed as the plan said, a series whose books came
  into Audiobookshelf before Fetchshelf would look wholly missing to the Follow job, and on *Auto* it would download
  copies that are already there. Each sync now keeps one `AudiobookshelfSeriesBooks` row per book and format it has,
  replaced on every sync. `LibraryQueries` adds those rows to the torrents' series rows, so the Follow job's owned
  check and the Series page both count them.
  - Books Fetchshelf linked and matched (`Torrents.AbsId`) are left out, so they aren't counted twice.
  - An item with neither audio nor an ebook counts as both formats: wrongly taking a book for owned only skips a
    download.
  - This table and the `Follows.Source` column are the migration.
- **Stopped series.** Stopping a follow deletes it, so the next sync would have followed the series again (found in the
  dev test). A stopped series is now remembered in `StoppedSeries` and skipped by the sync, and following it again by
  hand clears that. This is a second migration, as the dev database already had the first applied.
- **Series names.** A series name with a number Audiobookshelf kept from a folder (`Earthseed #1, 2`,
  `Doctor Who #The Krotons`) is cut at `" #"`, so it is followed once, under a name MAM's series have.
- **The real run.** The sync was run against the user's Audiobookshelf (three book libraries): 1,561 series became
  1,181 follows and 3,525 book rows in about a second, and a second sync added nothing.
- **Failures and dry runs.** An Audiobookshelf failure is logged and the run searches the follows it has; a dry run
  follows and stores nothing but says how many series it would add.
- **Release notes** are left for the release PR.
