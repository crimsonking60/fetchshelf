# Issue #1: Shelf import from Hardcover — implementation plan

## Context

Goodreads closed its RSS feeds, so the Goodreads shelf import was removed (commits `dbb2301`, `9779702`,
`b4b28f1`, October 2026). Issue #1 proposes [Hardcover](https://hardcover.app) instead. Its GraphQL API gives a
user access to their own library and lists.

The goal: books you mark *Want to Read* on Hardcover, or put on one of your Hardcover lists, are searched on MAM
and queued for download under the same safety rules as autograbs.

Decisions made:

- **Source:** each `[[hardcover_list]]` block names either a reading status (normally *Want to Read*) or one of
  your Hardcover lists.
- **Tracking:** every book is stored with its audiobook and ebook status, and a **Lists** page shows them, like
  the old Goodreads page.
- **Read only:** Fetchshelf never changes anything on Hardcover. The token only needs read permissions.

## The Hardcover API (from its documentation)

- **Endpoint:** GraphQL at `https://api.hardcover.app/v1/graphql`.
  - Requests carry an `authorization` header with a personal access token. Accept the token with or without a
    `Bearer ` prefix.
  - Send a `User-Agent` such as `Fetchshelf/<version>`, as Hardcover recommends.
- **Token:** created under Settings → Hardcover API, with an expiry and chosen permissions. Fetchshelf needs:
  - `read:me` to find your user id;
  - `read:library` (or `read:library:private`) for the reading statuses (`user_books`);
  - `read:lists` (or `read:lists:private`) for lists and their books (`lists`, `list_books`).
- **Reading statuses:** 1 Want to Read, 2 Currently Reading, 3 Read, 4 Paused, 5 Did Not Finish, 6 Ignored.
- **Data per book:**
  - `book { id title slug contributions { author { name } contribution } book_series { position series { name } } image { url } }`.
  - Lists give `list_books { book … position date_added }`.
- **Limits:**
  - Rate limits: per minute (free plan: 60, burst 10) and per day (5000).
  - Every top-level field in a query costs one request.
  - Queries time out after 30 s. `_like`/`_ilike`/`_regex` filters are disabled.
  - The responses carry `RateLimit` headers and, on 429, `Retry-After`.
- **Error codes:**
  - `401`: token missing, invalid or expired;
  - `403 insufficient_scope`: names the missing permission;
  - `429`: rate limited;
  - `503`: temporarily unavailable, safe to retry.
- **Status:** the API is in beta and "could break in the future"; tokens may be reset without notice. The
  integration must fail clearly, not quietly.

## Configuration

```toml
[hardcover]
token = "…"                    # or token_file = "/run/secrets/hardcover_token"

[[hardcover_list]]
name = "Want to Read"          # job name and Lists page title
status = "want_to_read"        # want_to_read | currently_reading | read | paused | did_not_finish
# list = "books-to-fetch"      # instead of status: the slug of one of your Hardcover lists
prefer_format = "audio"        # optional: audio | ebook; when both are found, only this one is grabbed
search_interval = 360          # optional, minutes; defaults to the global search_interval
dry_run = false

[[hardcover_list.grab]]        # one or more, tried in order: a cost plus the shared torrent filter
cost = "free"
languages = ["English"]
```

- Exactly one of `status` or `list` is required. `prefer_format` takes only `audio` or `ebook`. A
  `[[hardcover_list]]` without a `[hardcover]` section, or `[hardcover]` without `token` or `token_file`, is a
  startup error naming the problem, as every other config error does.
- `token_file` works like `mam_id_file`: it must be an absolute path, it is read after environment overrides, and
  it can be set as `FETCHSHELF_CONF_hardcover_token_file`.
- The Config page hides `token`, as it hides other secrets.
- The `grab` blocks are the same as the old Goodreads ones: a cost plus the shared torrent filter. `GrabEntry`
  was removed with the Notion lists, so it comes back (`git show dbb2301^:src/Fetchshelf.Data/Configuration/GrabEntry.cs`);
  the shared filter and its validation are reused unchanged.

## Approach

### 1. Hardcover client: new project `src/Fetchshelf.Clients.Hardcover`

Same shape as `Fetchshelf.Clients.Abs`: a typed `HardcoverClient` over a named `HttpClient("hardcover")`, plus
`HardcoverClientOptions` (token, user agent).

- `GetMeAsync()` returns the user id and username. It doubles as the token check.
- `GetBooksWithStatusAsync(userId, statusId)` and `GetListBooksAsync(userId, slug)`:
  - page through the results with `limit`/`offset` (for example 100 per page);
  - return a `HardcoverBook` record: id, title, slug (for the link), authors (only `contribution` null or
    "Author", not narrators or illustrators), series name and position, cover URL.
- The GraphQL query texts are constants. Variables go in the request body, never spliced into the query text.
- Errors:
  - `401` becomes `HardcoverAuthenticationException` ("the token is invalid or has expired").
  - `403` becomes `HardcoverAuthenticationException` naming the missing permission.
  - `429` waits `Retry-After` once and retries, then fails.
  - A GraphQL `errors` array becomes `HardcoverException` with its message.
  - A list slug that doesn't exist is an error naming the slug.
- Tests (new `tests/Fetchshelf.Clients.Hardcover.Tests`) use the existing `FakeHttpMessageHandler` pattern:
  - recorded responses;
  - paging;
  - author filtering;
  - each error case;
  - that the token goes in the header and the variables in the body.

### 2. Storage: bring back `Lists` and `ListItems`

The `RemoveGoodreadsLists` migration dropped them. A new migration, `AddHardcoverLists`, creates them again in a
neutral shape (`src/Fetchshelf.Data/Entities/ListEntities.cs`, restored from `dbb2301^` and adjusted):

- **`ListEntity`:**
  - `Id`, for example `hardcover:status:1` or `hardcover:list:<slug>`;
  - `Name`, `ListType = "Hardcover"`, `CreatedAt`, `UpdatedAt`.
- **`ListItemEntity`, keyed by `ListId` plus `ExternalId` (the Hardcover book id):**
  - title, authors (JSON), series name and number, cover and book URL, `PreferFormat`, `AllowAudio`,
    `AllowEbook`;
  - per side: `AudioMamId`, `AudioStatus`, `AudioAt` and the ebook equivalents;
  - `LastSearchedAt`, `CreatedAt`, `MarkedDoneAt`, and `RemovedAt` (no longer on the Hardcover shelf or list).
- Side statuses as before: `Selected`, `Wanted` (found, but costs more than the block allows), `NotWanted`
  (dropped by `prefer_format`), `Existing` (already in the library), plus `NotFound`.

A test upgrades a database from before the migration, with data, the same way the earlier list migrations are
tested (`c6b17f4`).

### 3. The importer: `src/Fetchshelf.Data.AutoDownloader/HardcoverListImporter.cs`

It is modelled on the removed `GoodreadsListImporter` (`git show dbb2301^:src/Fetchshelf.Data.AutoDownloader/GoodreadsListImporter.cs`).

**Helpers it reuses**, restored from `b4b28f1^` into `Fetchshelf.Core.AutoDownloader` with their tests (FuzzySharp
is still referenced there):

- `ListMamQueryBuilder` (title and authors to a MAM search text);
- `ListGrabSearchTranslator` (a grab filter to a MAM query, narrowed to the sides still needed);
- `ListCandidateRanker` (fuzzy title ≥ 80 and author > 90, series check, format preference);
- `ListLibraryMatcher` (is this book already in the library?).

**One run:**

1. Fetch the shelf or list from Hardcover *before* taking the MAM gate, so a Hardcover problem never holds up MAM
   jobs. Update `ListItems`: add new books, refresh changed ones, and set `RemovedAt` on books that left the shelf
   or list. A book that comes back is active again.
2. Take `MamAccessGate`.
3. For each active book that isn't marked done and has a side still needed:
   - check the library first (`Existing`);
   - otherwise search MAM with each `grab` block in turn, rank the candidates, and pass the winner to
     `SelectionRunner.ProcessCandidateAsync(record, metadata, ignoreTorrents, cost, autograbCategory: null,
     grabberName: entry.Name, dryRun)`;
   - apply `prefer_format` as before;
   - record `Selected`, `Wanted` or `NotFound` on each side.
   - The unsatisfied-slot budget comes from `SelectionBudget.Compute`, and the buffers come from `SafetyBuffers`
     when downloading, as for autograbs. A `[[hardcover_list]]` can't set its own buffers, so there is nothing new
     to validate.
4. Keep MAM polite: the same 400 ms between searches as before. A book whose last search found nothing is searched
   again at most once a day (`LastSearchedAt`), so a long shelf doesn't mean hundreds of MAM searches on every
   run.
5. Return a summary (selected, failed). When something was selected, the job triggers **Download**, as
   autograbs do. Failed books make the job show *Finished with errors*.

The log says what happened to each book, as the other jobs do.

Tests (`tests/Fetchshelf.Data.AutoDownloader.Tests/HardcoverListImporterTests.cs`) use a fake Hardcover client
and the existing fake MAM setup, and cover:

- new, changed and removed books;
- `Existing` from the library;
- selection, and `Wanted` for books that cost too much;
- `prefer_format`;
- `dry_run` writing nothing;
- the once-a-day re-search;
- a Hardcover failure not taking the MAM gate.

### 4. Jobs and wiring (`src/Fetchshelf.Web/Program.cs`)

- Register the `hardcover` HttpClient and `HardcoverClient` only when `[hardcover]` is configured, as for
  Audiobookshelf. `HardcoverListImporter` is scoped.
- One job per `[[hardcover_list]]`, named after its `name` or `hardcover_list[<index>]`, with an
  `IntervalJobScheduler` at `search_interval` (or the global one).
  - Its job description says it searches MAM for the books on that Hardcover shelf or list.
  - It triggers Download when something was selected.
- `UnconfiguredJobs`: add "Hardcover", so the Jobs page says when it isn't set up.

### 5. Lists page: `src/Fetchshelf.Web/Pages/Lists/`

Restored from `dbb2301^` and adjusted. The menu link comes back in `_Layout.cshtml`.

- **Index:** one row per Hardcover list: name, how many books, how many still missing a format.
- **Detail:** one card per book:
  - cover, title, authors, series;
  - a link to the book on Hardcover, and a MAM search link;
  - each wanted side's status, linking to the torrent when there is one;
  - **Mark done** / **Undo**.
  - Filter: all, any missing, audio missing, ebook missing. Books no longer on the shelf are hidden by default.
- It uses the page explanation partial, plain text in labels, and paging if a list is long (the shared pager).
- Tests follow the removed `ListsIndexPageTests` and `ListsDetailPageTests`.

### 6. Documentation and examples

- **`docs/configuration.md`:** a `[hardcover]` section (token, token file, required permissions, token expiry) and
  a `[[hardcover_list]]` section (fields, `grab` blocks, an example). `FETCHSHELF_CONF_hardcover_token(_file)` in
  the overrides list.
- **`docs/user-guide.md`:**
  - "Books from Hardcover": create the token with the needed permissions, set it up, and what happens on each
    run.
  - The **Lists** page.
  - Jobs table rows.
  - Troubleshooting: expired token (401), missing permission (403), rate limit.
  - Hardcover's API is in beta.
- **`docs/config-examples/5-everything.toml`:** a `[hardcover]` and `[[hardcover_list]]` example. A test
  already loads every example.
- **`README.md`:** the feature line; Untested list: "Hardcover lists", until you confirm them.
- **`CLAUDE.md`:**
  - The note that Goodreads and its Lists page were removed becomes "Goodreads was removed; Hardcover replaced it
    (issue #1)".
  - Add the new client project to the project graph.

## Order of work

Each step is one commit on `develop`, with build, tests and format passing:

1. Hardcover client project, with tests.
2. Restored list helpers in Core.AutoDownloader, with their tests.
3. Config: `[hardcover]`, `[[hardcover_list]]` with the restored `GrabEntry`, `token_file`, validation, Config
   page hiding the token, with tests.
4. Entities and the `AddHardcoverLists` migration, with an upgrade test.
5. `HardcoverListImporter`, with tests.
6. Jobs, wiring and `UnconfiguredJobs`.
7. Lists pages, with tests.
8. Documentation, examples, README and CLAUDE.md.

Release through the usual release branch and pull requests.

## Risks

- **The API is in beta.** Field names or permissions may change, and tokens can be reset.
  - Mitigation: one place holds the queries, errors are explicit (401/403 shown on the Jobs page and in the
    log), and the docs say it's beta.
  - Real responses are recorded once during step 1 and kept as test fixtures.
- **Rate limits:** a shelf of a few hundred books is a few requests per run (paged), far below 60 a minute and
  5000 a day. `Retry-After` is honoured once.
- **MAM load:** bounded by the unsatisfied-slot budget, the 400 ms delay, the MAM gate and the once-a-day
  re-search of books that weren't found.
- **Matching quality:** Hardcover titles can differ from MAM's (subtitles, series in the title). The ranker's
  thresholds are the ones the Goodreads import used; the Lists page's MAM search link and **Mark done** cover the
  misses.
- **Commercial-use terms:** Hardcover forbids commercial products from using users' data. Fetchshelf is
  self-hosted and only reads the user's own data with their own token, which is allowed.

## Verification

- **Gates:** `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.
- **Unit and integration tests** as listed in each step, including the migration upgrade test and loading the
  example config.
- **Manual test with a real Hardcover account:**
  - Create a token with only `read:me`, `read:library` and `read:lists`. Mark three books *Want to Read*:
    - one you already have in the library;
    - one that is free on MAM;
    - one that isn't on MAM.
  - Add one book to a list called `books-to-fetch`, and configure two `[[hardcover_list]]` blocks.
  - Run each job with **Run now**:
    - the Lists page shows *Existing*, *Selected* (then downloaded by Download) and *Not found*;
    - the list's book is processed too;
    - the log has one line per book.
  - Run again: nothing is searched twice. The not-found book isn't searched again within the day.
  - Remove a book from *Want to Read* on Hardcover and run: it's hidden on the Lists page.
  - With `dry_run = true` nothing is written. A token without `read:lists` gives a clear 403 message naming the
    permission. An expired or wrong token gives the 401 message. Neither blocks the other jobs.
