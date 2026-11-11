# Account page: the MAM numbers every safety rule works from — implementation plan

## Context

Every safety rule in Fetchshelf works from the MAM account's numbers: the ratio budget from uploaded, downloaded
and `min_ratio`; the selection limit from the unsatisfied-snatch count and limit and `unsat_buffer`; wedge use from
the wedge count and `wedge_buffer`. `MamClient.GetAccountStateAsync` reads them from `jsonLoad.php` (cached for 60
seconds) into `MamAccountState`, and the autograb, Hardcover list, snatchlist and Download jobs use them.

None of it is shown anywhere. "Why did nothing download?" is answered by these numbers and the budgets worked out
from them, but they only appear in log lines (`Ratio budget for this run: …`, `this run's limit is 0
selections (…)`). Neither is it shown when MAM calls are paused after a 403 or a 429, or when `mam_id` last worked:
`MamClient` keeps `_forbiddenUntil` and `_rateLimitedUntil` private.

## Change

### 1. More from the account endpoint

`MamAccountState` gains the fields `jsonLoad.php` returns that a user recognizes from MAM's own pages, each
optional and read only when present, so a field MAM drops or renames doesn't break the account read:

- `Username` (`username`, a string)
- `ClassName` (`classname`, a string, such as "Power User" or "VIP")
- `BonusPoints` (`seedbonus`, a number)

These names and types were checked against a real `jsonLoad.php?snatch_summary=true` answer on 2026-11-11. Its
top-level fields are `classname`, `country_code`, `country_name`, `downloaded`, `downloaded_bytes`, `ratio`,
`seedbonus`, `snatch_summary`, `uid`, `uploaded`, `uploaded_bytes`, `username` and `wedges`. That account is a
lifetime (Elite) VIP, which likely explains why it has no VIP expiry. An account with a time-limited VIP may get
one, under a name that couldn't be checked, so none is read until an answer with it has been seen. The country and the preformatted `uploaded`/`downloaded` strings are not used: the
page formats the byte counts itself, like the rest of the UI.

The fields already read stay required: the safety rules can't work without them. A test fixture is made from that
answer, with the personal values replaced.

**The snatch summary.** `snatch_summary` holds one entry per kind of snatch MAM counts, each an object with a
`count`, MAM's own label in `name`, and mostly a `size`. Seen on 2026-11-11:

- the unsatisfied snatches: `unsat`, which also has the `limit` Fetchshelf already reads;
- seeding: `seedUnsat`, `seedHnr` and `sSat`;
- inactive: `inactUnsat`, `inactHnr` and `inactSat`;
- `leeching`;
- uploads: `upAct` and `upInact`;
- `duplicates`, which has no size;
- `reseed`, which has `inactive` instead of a size;
- `ite`, which has `latest` instead of a size.

It also holds a few flat values: `connectable`, `created`, `created_at`, `ipv6_mac`, `partial`, `recently_deleted`
and `v6_connectable`.

- `MamAccountState` gains `SnatchKinds`: every object entry in `snatch_summary` with a `count`, read generically as
  key, `name`, `count` and `size`. A new kind MAM adds then shows without a code change, and a missing one is just
  absent. `size` is a number of bytes or `null` (checked 2026-11-11); `null` shows as "-".
- The labels shown are MAM's `name` values, with runs of spaces collapsed ("Seeding -  Uploads" has two). They read
  as they do on MAM's own pages. The labels seen on 2026-11-11:

  | Key | MAM's label |
  |---|---|
  | `unsat` | Unsatisfied |
  | `seedUnsat` | Seeding - Not Yet Satisfied |
  | `seedHnr` | Seeding - H&R - Not Yet Satisfied |
  | `sSat` | Seeding - Satisfied |
  | `inactUnsat` | Not Seeding - Not Yet Satisfied |
  | `inactHnr` | Not Seeding - H&R - Not Yet Satisfied |
  | `inactSat` | Not Seeding - Satisfied |
  | `leeching` | Leeching Torrents |
  | `upAct` | Seeding - Uploads |
  | `upInact` | Not Seeding - Uploads |
  | `duplicates` | Duplicate peer entries |
  | `reseed` | Reseed requests |
  | `ite` | Important Tracker Errors |

- **Three need attention**, shown first as warnings when their count is above 0, each linking to MAM's snatch list:
  - `inactHnr`, a hit-and-run;
  - `inactUnsat`, not seeding and not yet satisfied: these become hit-and-runs if left;
  - `ite`, important tracker errors.

  The keys, not the labels, decide this, so a reworded label doesn't silence a warning.
- `connectable` is shown in the connection part, under its label.

### 2. What `MamClient` knows about its own state

Read-only, thread-safe properties on `MamClient`:

- `LastAccountState`: the most recent `MamAccountState` with when it was fetched, or null before the first read.
  It is the value already cached; it is kept after the 60-second cache runs out.
- `PausedUntil` and `PauseReason`: set while calls are paused after a 403 (`mam_id` refused) or a 429 (rate
  limited), from the existing `_forbiddenUntil` and `_rateLimitedUntil`.
- `LastSuccessAt`: when MAM last answered any request with success, set in `SendAsync`.
- `LastRefusal`: the time and text of the most recent 403 or 406.

These fields are written from jobs and page requests at once. They are kept in one lock-guarded object, which also
covers the existing unsynchronized writes noted in the project review (A5).

### 3. The page

A new **Account** page (`Pages/Account/Index.cshtml`, `/account`), in the navigation after **Jobs**, shown only
when a `mam_id` is set (`FetchshelfSettings.BackgroundJobsEnabled`), as **Lists** is only shown with Hardcover
lists.

**From MAM** (as of the time shown, from the last read):

| | |
|---|---|
| Account | username, class |
| Ratio | 2.84 |
| Uploaded / Downloaded | 1.21 TiB / 436.0 GiB |
| Unsatisfied snatches | 14 of 20 |
| Wedges | 37 |
| Bonus points | 41,250 |

Below it, **Snatches**: one row per kind from the snatch summary, with MAM's label, the count and the size, in the
order MAM gives them, and the inactive hit-and-run warning on top when there are any.

**What Fetchshelf works out from it**, each with the formula in words below the number, the way the log explains
it:

- **Ratio budget:** how much ratio-cost downloading `min_ratio` still allows, `uploaded / min_ratio − downloaded −
  in flight`, with the in-flight size of torrents started but not finished. "0 B: the ratio is at or below
  `min_ratio`" when it is spent. This uses `DownloadBudget.ComputeRatioBudgetBytes` and `DescribeRatioBudget`
  as the Download job does, and the same in-flight sum, moved from `DownloadRunner` to a shared query.
- **Selection slots:** `limit − count − unsat_buffer`, and for each autograb with its own `unsat_buffer` or
  `max_active_downloads`, its own line. This uses `SelectionBudget.Compute` and `Explain`.
- **Wedges to spend:** `wedges − wedge_buffer`, and per autograb with its own `wedge_buffer`.
- **Queue:** how many selections wait on the **Selected** page, and how many of them the current numbers would let
  start.

**Connection to MAM:**

- "Last answered successfully 3 minutes ago."
- When paused: "MAM calls are paused until 14:32: MAM refused `mam_id` (HTTP 403)", with what to do, as the log
  says it.
- The last refusal, if any, with its time.

**Refresh:** the page shows the last read without calling MAM. **Refresh from MAM** reads the account again. It
takes the `MamAccessGate`, as every MAM call must. When a job holds the gate, it waits up to 5 seconds, then says
"MAM is busy with the job *Autograb Freeleech*; the numbers below are from 14:02" instead of waiting for the job.
This needs the gate to say who holds it: `MamAccessGate.AcquireAsync` takes an owner name, and a `TryAcquireAsync`
with a timeout is added. When nothing has been read since the start, the page offers the button in place of the
numbers.

### 4. A strip on the Jobs page

Above the job list: ratio, unsatisfied *count of limit*, wedges, the ratio budget, and the pause notice when
paused, from the last read, linking to the Account page. The Jobs page is where "why didn't the autograb select
anything?" is asked.

## Tests

- `MamClient`:
  - The optional fields are read when present and left null when missing, using the captured fixture.
  - Every snatch kind in the fixture is read with its label, count and size. An entry without a `count`, and a flat
    value such as `partial`, is skipped. A kind missing from the answer is just absent.
  - `LastAccountState` survives the cache running out.
  - `PausedUntil` and `PauseReason` are set by a 403 and a 429 and cleared when the pause ends.
  - `LastSuccessAt` is updated by a successful search.
- `MamAccessGate`: `TryAcquireAsync` times out while another owner holds it and reports that owner; the owner is
  cleared on release.
- The derived numbers come from the existing Core functions; one test for the page model checks that each is
  wired to the right inputs (global and per-autograb buffers, in-flight bytes).
- Web tests:
  - The page with a recorded account state shows each number and each derived budget.
  - The snatch table shows MAM's labels, with collapsed spaces and "-" for a null size.
  - A count above 0 for `inactHnr`, `inactUnsat` or `ite` shows its warning first. With all three at 0, there's no
    warning.
  - A paused client shows the pause notice.
  - **Refresh** with a busy gate shows the busy message and keeps the old numbers.
  - Without a `mam_id`, there is no Account link and the page says the auto-downloader isn't configured.
  - The Jobs page strip shows the numbers and links to the page.

## Documentation

`docs/user-guide.md`: an **Account** page section (each number, each budget and its formula, what a pause means
and what to do), the Jobs page strip, and a pointer from *Nothing is being downloaded* under Troubleshooting.

## Release notes

**Added:** the Account page and the Jobs page strip. **Upgrading:** nothing to do; no database migration. Written
at release time.

## Out of scope

- History: storing a snapshot a day and drawing ratio, upload and bonus points over time. It is a small follow-up
  once the page exists, and needs a table and a migration.
- Reading the account on a timer of its own. The page shows what the jobs last read, plus **Refresh**; the jobs
  read it on every run.
- Spending bonus points (review point 5.7).
