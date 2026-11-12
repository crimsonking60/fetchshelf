# A Statistics page and a Prometheus `/metrics` endpoint — implementation plan

## Context

Everything needed for library and download statistics is stored already:

- `Torrents`: media type and language as columns, authors and series as rows (PR #47), size in the metadata,
  `CreatedAt`, `LibraryPath`/`LinkerName`, chosen format.
- `Events`: `Grabbed` with `Cost`, `WedgeUsed`, `GrabberName`, `OccurredAt`; `Linked`, `Removed…`.
- `JobRuns`: every run with its result and counts.
- `MamClient.LastAccountState`: ratio, upload, download, wedges, unsatisfied, bonus points.

None of it is shown as totals or over time, and there is nothing a monitoring stack can read.

## Change

### 1. Statistics page

`Pages/Statistics/`, linked from the navigation after **Account**. Numbers and tables, no chart library: bars as
CSS widths, like the rest of the UI's server-rendered style.

- **Library:** books by media type, by language, by library; total size; added per month for the last 12 months.
- **Downloads:** grabbed per month by cost (free, VIP, freeleech, wedge, ratio); GB downloaded per month on ratio
  versus free; wedges spent per month; grabs per autograb, list, follow and upgrade (`GrabberName`, grouped by kind).
- **Jobs:** per job, runs and failures in the last 30 days.

Queries live in `LibraryStatistics` (Data), one method per table, each a single grouped query. Months are grouped in
UTC from the event's stored time; SQLite can't group `DateTimeOffset` values, so each query selects the minimal
columns and groups in memory, which is fine at 10,000 torrents and a few thousand events a month. The page is
computed per request; a 60-second memory cache keeps reloads cheap.

### 2. `/metrics`

Optional, `[metrics]` with `enabled = true`; off by default. Plain Prometheus text exposition written by a small
formatter (no new dependency):

| Metric | Type | From |
|---|---|---|
| `fetchshelf_mam_ratio`, `_uploaded_bytes`, `_downloaded_bytes`, `_wedges`, `_unsatisfied`, `_unsatisfied_limit`, `_bonus_points` | gauge | last account state |
| `fetchshelf_ratio_budget_bytes` | gauge | as the Account page computes it |
| `fetchshelf_selected_waiting` | gauge | queued selections |
| `fetchshelf_torrents{media_type,library}` | gauge | torrent counts |
| `fetchshelf_job_last_run_timestamp_seconds{job,result}`, `fetchshelf_job_failing{job}` | gauge | job registry |
| `fetchshelf_grabbed_total{cost}`, `fetchshelf_linked_total` | counter | events |
| `fetchshelf_mam_paused` | gauge | 1 while MAM calls are paused |

- No call to MAM or qBittorrent on a scrape: only what is stored or cached. Unknown values (no account read yet)
  are left out, not written as 0.
- `/metrics` follows `allowed_hosts` (`plans/allowed-hosts-and-opds-login.md`) like every endpoint, and has no login,
  like the UI.

## Tests

- `LibraryStatisticsTests`: each table on a seeded database, including months with nothing.
- `StatisticsPageTests`: renders with an empty database and a seeded one.
- `MetricsFormatterTests`: exposition format, label escaping, left-out unknowns.
- `MetricsEndpointTests`: 404 when disabled; values from a seeded database and a fake account state; no MAM request
  made.

## Documentation

- `docs/user-guide.md`: the Statistics page in the pages table and its own section; the pages table and `CLAUDE.md`'s
  page list gain `Statistics/`.
- `docs/configuration.md`: `[metrics]`, with the metric list and a Prometheus scrape example.

## Release notes

- Added: a Statistics page with the library by media type, language and library, and downloads by month and cost.
- Added: an optional Prometheus `/metrics` endpoint.
- Upgrading: nothing to do.

## Out of scope

- Ratio, upload and bonus points over time: `plans/account-snapshots.md` shows them on the Account page, and the
  Statistics page links there.
