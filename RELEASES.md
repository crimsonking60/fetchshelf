# Fetchshelf releases

Every Fetchshelf release, newest first. Versions are `YYYY.MM.N`: the year and month of the release, and its
number within that month. Container images are published as `ghcr.io/crimsonking60/fetchshelf:<version>`.

Before upgrading, read the **Upgrading** notes of every release between your version and the new one.

## 2026.10.3 - 2026-10-31

Goodreads support is gone, there is a new Duplicates page, images now run on ARM, and Docker can tell
whether Fetchshelf is healthy.

### Upgrading

- **Remove Goodreads from `config.toml` before you upgrade.** Fetchshelf no longer starts with a
  `[[goodreads_list]]` block or a `goodreads_interval` setting in it, and says which setting is the problem.
  Notion lists use `import_interval`.
- The database is upgraded when the new version starts: the stored Goodreads list data is deleted, and going
  back to an older version is not possible. Back up your `/data` folder first.
- If you set up Audiobookshelf from the user guide's Docker Compose example, check two things, because the
  example was wrong. Audiobookshelf has to see your library at the same path as Fetchshelf, for example
  `/media/library/audiobooks` in both containers, or no book is ever matched. And `[audiobookshelf]` `url`
  should be the address you open Audiobookshelf at in a browser, such as `http://192.168.1.10:13378`.

### Added

- **Duplicates** page: the torrents that were skipped because you already have, or have already selected, the
  same book in an equal or better format, and what each one duplicates.
- Images for 64-bit ARM (`linux/arm64`, for example a Raspberry Pi 4 or 5) and 32-bit ARM (`linux/arm/v7`),
  as well as `linux/amd64`. Docker picks the right one for your machine.
- A health check: `docker ps` shows the container as healthy or unhealthy, and `/health` answers `Healthy`
  or `Unhealthy` for monitoring tools.
- The version is shown on the **Config** page and in the first log line, for bug reports.
- The **Jobs** page lists the kinds of job you haven't configured.
- **Selected** page: change the cost of a queued torrent to `Ratio`, `TryWedge` or `UseWedge`.
- Torrent page: change the torrent's category and tags in qBittorrent, and a link to the torrent on MAM.
- **Torrents** page: click a media type to show only that kind. The filters in use are listed above the
  table, each with a link to remove it.
- The log shows when each job starts and finishes, and why a job failed. Failures were only shown on the
  **Jobs** page before.

### Fixed

- MAM download links contain a personal download key, and they were written to the log. They aren't any
  more, and neither is the address of every other web request Fetchshelf makes. Logs from earlier versions
  can contain these links, so don't share them unedited.
- `add_torrents_stopped = true` works with qBittorrent 5. Torrents were added started.
- `docker stop` gives Fetchshelf Docker's full stop timeout to shut down. It was killed after 2 seconds.
- **Torrents** page: sorting, clicking a language, searching and the bulk actions keep your search, filters
  and sort. They used to start again from the full list.

### Removed

- Goodreads lists (`[[goodreads_list]]` and `goodreads_interval`). Goodreads no longer offers the RSS feeds
  they were read from.
- The **Lists** page, which only ever showed Goodreads lists.

## 2026.10.2 - 2026-10-29

A safety fix for `dry_run`, and Notion lists of any size.

### Upgrading

- If you ran 2026.10.1 with `dry_run = true` on an `[[autograb]]` or `[[snatchlist]]` block, that block was
  not actually dry: autograbs queued real downloads and snatchlists recorded torrents as owned. Check the
  Selected page and qBittorrent for anything you didn't mean to download.
- Goodreads lists don't work any more: Goodreads (owned by Amazon) discontinued all RSS feeds in October 2026,
  and Fetchshelf reads shelves through them. Every `[[goodreads_list]]` now fails with an HTTP 404 error.
  Remove or comment out those blocks. Notion lists are unaffected.

### Changed

- Every autograb, snatchlist and Notion list run logs a one-line summary of what it found and did. Set
  `FETCHSHELF_LOG=Debug` to also see what happened to each torrent and why.

### Fixed

- `dry_run = true` on `[[autograb]]` and `[[snatchlist]]` blocks now works. A dry run searches and runs every
  selection check, but selects, records and downloads nothing, and logs what each torrent would get. Your
  ratio, unsatisfied-snatch slots and wedges are only checked when a download starts, so "would be selected"
  means it would be queued, not necessarily started.
- Notion lists read every row of the database. Only the first 100 rows were read before.
- A Notion list's dry run runs the same selection checks, and logs for each link whether it would be selected
  and with which `grab` block, or why not.

## 2026.10.1 - 2026-10-28

The first published release, made mainly to try out the release process. Fetchshelf is still under active
development, so expect rough edges.

### Upgrading

- If you ran a development build before, your database is upgraded automatically on first start. Repeated
  errors on the Errors page are reduced to one per torrent and job, keeping the newest.

### Added

- Container images on the GitHub Container Registry: `ghcr.io/crimsonking60/fetchshelf:2026.10.1` and
  `:latest`.
- A [user guide](docs/user-guide.md), a [configuration reference](docs/configuration.md) and six annotated
  [example configurations](docs/config-examples/), from minimal to complete.
- `max_authors` in the torrent filter, to skip anthologies and other many-author torrents.
- `torrents` as another name for the `new` autograb type, and `newest_first` as an explicit autograb `sort_by`.
- Page navigation on the Torrents page.
- Errors page: tick several errors and remove them at once.
- Selected page: tick several selections and remove them at once.
- Torrents page: a Check qBittorrent button.

### Changed

- Four new themes: Dark Warm (the default), Light Warm, Dark Cool and Light Cool.
- Errors page: each error shows the torrent's name, linked to the torrent when Fetchshelf tracks it, and its
  job (Download, Link or Clean). A torrent that keeps failing has one row that is updated, not a new row on
  every run, and the row disappears once the torrent links. "Restore the selection" is now
  **Retry download**.
- Jobs page: Download, Link and Clean come first, then the other jobs alphabetically.
- Selected page: a selection leaves the queue once its torrent has finished downloading, and selections that
  have started can no longer be edited.
- The Search page shows its results without reloading the page, and downloads start in place.
- Every timestamp is shown in local time as `YYYY-MM-DD HH:MM:SS`.
- The log no longer lists every database query and web request; those sources only log warnings and errors.

### Fixed

- The web UI no longer freezes when the terminal Fetchshelf logs to stops reading its output, which made
  every page hang during Link runs.
- Musicology torrents are linked using `music_types` and Radio torrents using `radio_types`, so, for
  example, a Musicology torrent of flac files is no longer refused.
- An autograb's `search_in` is now passed on to the MAM search instead of being ignored.
- MAM failures on the Search page are shown as plain messages with a Try again button.
- The Jobs page columns no longer shift every time the page refreshes.
