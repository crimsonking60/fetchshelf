# Job failures: on the Errors and Jobs pages, and a marker on every page — implementation plan

Builds on `plans/done/job-runs.md`, which keeps job runs in the database. That plan is implemented first.

## Context

The **Errors** page lists failures of three stages, one row per torrent (`ErroredTorrentEntity`, `ErrorStage`
`Grabber`, `Linker` and `Cleaner`, shown as Download, Link and Clean). Everything else that fails is only in the
log:

- **Item failures of the other jobs.** The snatchlist importer counts a row it can't import, the Hardcover list
  importer a book it can't search for, and the Audiobookshelf matcher a book it can't match. Each logs it,
  and the job's result says "*N* … could not be …; see the log". Which ones, and why, takes a search through the
  log. The next run tries them again, with no sign of whether they keep failing.
- **Whole-job failures.** A run that throws, such as "MAM refused the request (HTTP 403)", "the connection to
  qBittorrent failed" or a Hardcover token that expired, shows as `Error: …` in the job's row on the Jobs page.
  Until `plans/done/job-runs.md`, that row was gone after a restart. Even with it kept, nothing outside the Jobs page
  shows that something is wrong: a user who opens Search or Torrents sees nothing amiss while the autograbs have
  been failing for two days.

## Change

### 1. Item failures of the other jobs on the Errors page

`ErrorStage` gains three stages, each with its own key, using the existing row shape (key, title, error text,
time, one row per stage and key, cleared when that item succeeds):

| Stage | Shown as | Key | Title | Recorded by |
|---|---|---|---|---|
| `Snatchlist` | Snatchlist | MAM id | the row's title | `SnatchlistImporter`, where it counts `failed` |
| `HardcoverList` | Hardcover list | list id and Hardcover book id (`ListId` + `ExternalId`, the item's key), joined with a space | the book's title | `HardcoverListImporter`, where it counts `failed` |
| `Audiobookshelf` | Audiobookshelf | info-hash | the torrent's title | `AbsMatcher`, where it counts `failed` |

- **Each importer clears an item's row when the item succeeds**, as the Link job does, so the page shows what is
  failing now, not what once failed.
- **No migration is needed**: `Stage` is stored as an integer and the new values come after the existing ones. A
  database test proves that rows of the old stages read back unchanged.
- **A failure that stops the whole run is not an item failure.** The snatchlist importer's stop on 403, 406 or 429
  stays a job failure, below, not a row per remaining item.
- **The job results point to the page:** "*N* books could not be searched for; see the Errors page" instead of
  "see the log".
- **Actions:** **Remove** works as for every row.
  - A *Hardcover list* row links to the book on the **Lists** page.
  - An *Audiobookshelf* row and a *Snatchlist* row of a stored torrent link to the torrent's page.
  - No retry button: these jobs try every failed item again on their next run.
- **The stage filter** lists the new stages. Their rows are only possible when that job is configured.

### 2. Whole-job failures

A job whose latest run ended in `Failure` (not `CompletedWithErrors`, not `Cancelled`) counts as **failing** until
a later run succeeds. From the `JobRuns` table, a failing job also has a since: the start of the first failed run
after its last success, and how many runs have failed since.

**The Errors page** gets a section above the item rows, *Jobs that are failing*, one line per failing job:

- the job (linking to its history page from `plans/done/job-runs.md`);
- the error of its latest run;
- since when it has been failing, and how many runs in a row;
- **Run now**, as on the Jobs page.

It is not a table of its own and has no **Remove**: it shows the latest run of each job, which a successful run
replaces.

**The Jobs page** shows a failing job's row in the error style, with "failing since 2026-11-12 08:30 (6 runs)"
under its result.

### 3. A marker on every page

The navigation's **Errors** link carries a count when anything needs attention: failing jobs plus item rows
recorded or updated in the last 24 hours. "Errors (3)", with the count styled as a small badge and a
`title` saying what it counts ("1 job failing, 2 new errors").

- Item rows older than a day still show on the Errors page, but don't keep the badge lit. A torrent that has
  failed to link for a month has been seen; the badge is for what changed.
- **Cost:** one small query per page view. Failing jobs come from the registry, which holds each job's latest
  run in memory, so they cost no query. Recent item rows are a count of `ErroredTorrents` with `OccurredAt` in the
  last day. SQLite can't compare `DateTimeOffset` in a
  query, so the count is over the table's rows in memory; the table holds one row per failing item, so it stays
  small. A view component (`ErrorBadgeViewComponent`) in the layout does both.

### 4. The MAM refusal case

A 403 (`mam_id` refused) or 406 (account blocked) stops every MAM job at once, each with its own failure. The
Errors page groups them: when several jobs fail with a `MamForbiddenException` or `MamAccountBlockedException`,
one line says "MAM refused `mam_id` (HTTP 403) since 08:30: Autograb Freeleech, Snatchlist Unsat and 2 more" with
what to do. This needs the run record to keep the exception's kind: `JobRuns` gets a `FailureKind` column
(`MamRefused`, `MamBlocked`, `MamRateLimited`, `Other`), set by the registry from the exception type. The exception
types live in `Fetchshelf.Clients.Mam`, which Core can't reference, so `Program.cs` passes the registry a small
classifier function.

## Tests

- Each importer:
  - `SnatchlistImporter`: a row that throws records a `Snatchlist` error with its MAM id and title; the same row
    succeeding on the next run clears it; a 403 records no item rows.
  - `HardcoverListImporter`: the same with a list item.
  - `AbsMatcher`: the same with a torrent, including "no author to search for".
- Failing jobs as a pure function over a job's run history (`JobFailureState`, Core):
  - latest run failed → failing, since the first failure after the last success, with the count;
  - latest run succeeded, completed with errors, or was cancelled → not failing;
  - never succeeded → failing since its first run.
- The MAM refusal grouping: three jobs failing with `MamRefused` give one line naming them; one `Other` failure stays
  its own line.
- Database: rows with the old stages read back unchanged; a `JobRuns` row keeps its `FailureKind`.
- Web tests:
  - Errors page: the failing jobs section; the new stages in the list and the filter; the links from Hardcover list
    and Audiobookshelf rows.
  - Jobs page: a failing job's row says since when.
  - Layout: the badge counts failing jobs and recent rows, is absent when there are none, and leaves out rows
    older than a day.

## Documentation

`docs/user-guide.md`:
- **Errors** page: the three new stages, what each means and when it clears; the failing jobs section; the MAM
  refusal line.
- The badge on **Errors** in the navigation.
- **Jobs** page: the failing style and line.
- Troubleshooting: "something is wrong but nothing says so" now points to the badge.

## Release notes

**Added:** snatchlist, Hardcover list and Audiobookshelf failures on the Errors page; failing jobs on the Errors and
Jobs pages; the badge. **Upgrading:** a migration adds the failure kind to the job runs; no going back. Written at
release time.

## Out of scope

- Telling anyone outside the web UI: see `plans/notifications.md`.
- Failures of the web pages themselves (a search that fails shows its error on the page already).
