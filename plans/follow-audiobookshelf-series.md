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
