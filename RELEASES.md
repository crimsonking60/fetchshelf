# Fetchshelf releases

Every Fetchshelf release, newest first. Versions are `YYYY.MM.N`: the year and month of the release, and its
number within that month. Container images are published as `ghcr.io/crimsonking60/fetchshelf:<version>`.

Before upgrading, read the **Upgrading** notes of every release between your version and the new one.

## 2026.11.3 - 2026-11-05

Hardcover lists replace the Goodreads shelf import, the Torrents page gets filters and can set the qBittorrent
category of several torrents at once, and the Search page shows which torrents qBittorrent already has, with an
**Import** button for them.

### Upgrading

- **Files are copied where they can't be hardlinked.** `link_method`, and a library's `method` when it sets
  none, now default to `hardlink_or_copy`. Such a library used to fail with *Cross-device link* when the
  download and the library were on different filesystems or mounts; now it copies the files, which takes as
  much space again. Set `link_method = "hardlink"` to keep the old behaviour.
- **A Search page download starts straight away.** It used to wait for the next scheduled Download run, up to
  `search_interval` minutes. Set `download_after_selection = false` to leave all selections, from autograbs and
  Hardcover lists too, waiting on the **Selected** page for the scheduled run.
- The **AbsMatcher** job is now called **Audiobookshelf**, on the Jobs page and in the log.
- The database is upgraded when the new version starts, and going back to an older version is not possible.
  Back up your data folder first.

### Added

- Hardcover lists: a `[[hardcover_list]]` follows one of your reading statuses on
  [Hardcover](https://hardcover.app), such as *Want to Read*, or one of your Hardcover lists, and searches MAM
  for each book on it with the usual selection rules. Needs a `[hardcover]` section with an API token. The
  **Lists** page is back and shows what was found for each book (#7).
- `download_after_selection`: whether the Download job starts as soon as something is selected (default
  `true`).
- **Torrents** page: filters for status (linked, waiting, replaced, unlinked, metadata only), library,
  qBittorrent category, problems (library mismatch, not in qBittorrent), media type and language, on a row
  below the search box, with **Clear filters**.
- **Torrents** and **Events** pages: *Per page* 25, 50, 100, 500 or *All*, remembered for each list.
- **Torrents** page: **Set category** gives the ticked torrents one of the qBittorrent categories your
  libraries use. The category on a torrent's page is the same dropdown.
- **Search** page: each result says whether qBittorrent has the torrent and in which category, also when you
  added it outside Fetchshelf (this needs qBittorrent 5), or that qBittorrent no longer has one Fetchshelf
  downloaded. **Import** replaces the download buttons on a torrent qBittorrent has but Fetchshelf doesn't
  know: it records the torrent without downloading it.

### Changed

- **Hide downloaded torrents** on the Search page also hides torrents qBittorrent has.
- A torrent the Download job finds already in qBittorrent, in whatever category and finished or not, is now
  recorded, so it shows on the **Torrents** page and is linked once its category matches a library.
- A torrent Fetchshelf knows but no library accepts is flagged *No library* in the Mismatch column and logged
  with its category, instead of waiting without a word. Its page says how to get it linked.

### Fixed

- The Download job used a wedge on a torrent qBittorrent already had: it fetched the torrent file with the
  wedge before checking. It now checks first.
- A torrent still downloading in qBittorrent when the Download job found it was never recorded, so it was
  never linked.
- The Link job didn't save the current qBittorrent category of a torrent no library accepts, so the
  **Torrents** page showed an old one.
- A linked torrent whose category was changed to another library's showed that other library in the
  **Library** column, next to the *Different library* flag that said it wasn't there. The column now shows the
  library the files are in, and corrects itself on the next Link run. **Relink selected** moves such torrents to
  the library their category points to.

## 2026.11.2 - 2026-11-03

You can now take a book out of your library while qBittorrent keeps seeding it, and relink several torrents
at once. Changed `unsat_buffer` and `wedge_buffer` values now also apply to torrents already queued, and the
Link job no longer puts back copies the Clean job retired.

### Upgrading

- **An `[[autograb]]` that sets its own `unsat_buffer` or `wedge_buffer` needs a `name`.** Without one,
  Fetchshelf doesn't start, and the error says so.
- The database is upgraded when the new version starts, and going back to an older version is not possible.
  Back up your data folder first.

### Added

- **Unlink** on a torrent's page, and **Unlink selected** on the **Torrents** page: remove what Fetchshelf
  placed in the library, and the Audiobookshelf item, while qBittorrent keeps the torrent. Fetchshelf still
  counts the book as owned, and the Link job leaves it alone. **Refresh and relink** or **Relink selected**
  bring it back.
- `on_unlinked` on a `[[qbittorrent]]` entry: the category and tags to give a torrent you unlink, like
  `on_cleaned`.
- **Relink selected** on the **Torrents** page: relink the ticked torrents from the details Fetchshelf already
  has, without asking MAM. One that fails doesn't stop the rest, and the page says how many were relinked and
  why any weren't.

### Changed

- The Download job reads `unsat_buffer` and `wedge_buffer` from the current settings when it starts each
  torrent, using the autograb's own values when it has them. Torrents already waiting on the **Selected**
  page used to keep the values in force when they were queued.
- A *Cleaned* event says when `on_cleaned` couldn't be applied because qBittorrent doesn't have the torrent.

### Fixed

- The Link job linked copies retired by the Clean job straight back, and Clean then left them next to the
  better copy. The first Clean run after upgrading retires any such copies again.

## 2026.11.1 - 2026-11-02

The Docker database folder moves to `/database`, Notion lists are gone, and the Clean job no longer mistakes a
different book by the same author for a copy. Every job now logs what it does with each torrent.

### Upgrading

- **Mount your data folder at `/database` instead of `/data`.** Only the container side changes: keep your
  host folder and write, for example, `-v /srv/fetchshelf/data:/database`, or
  `- /srv/fetchshelf/data:/database` in Compose. Without this, Fetchshelf starts with a new, empty database.
  If you set `FETCHSHELF_DATA_FILE`, change it to the new path too.
- **Remove Notion from `config.toml`.** Fetchshelf no longer starts with a `[[notion_list]]` block or an
  `import_interval` setting in it, and says which setting is the problem.
- **Snatchlists now follow `cost`.** With the default, `metadata_only`, a snatchlist only refreshes torrents
  Fetchshelf already has. Until now it also recorded the rest as owned books; set
  `cost = "metadata_only_add"` on the block to keep that.
- Misspelled `sort_by` and `search_in` values now stop Fetchshelf at startup with an error naming them. They
  used to make that autograb fail on every run instead.
- The database is upgraded when the new version starts, and going back to an older version is not possible.
  Back up your data folder first.
- If an earlier version's Clean job replaced a book with a different book by the same author (see Fixed),
  find its *Cleaned* event on the **Events** page and use **Refresh and relink** on the replaced torrent to
  put it back.

### Added

- `link_method` at the top level of `config.toml`: how files are placed in every `[[library]]` that does not
  set its own method.
- `mam_id_file`: read the MAM session id from a file, such as a Docker secret, instead of `config.toml`.
- **Events** page: pages, like the **Torrents** page. The pager on both has **First** and **Last**.
- The log says what the Link, Download, Clean, snatchlist and Audiobookshelf jobs do with each torrent, and
  whether a link hardlinked, copied or symlinked the files.
- **Jobs** page: a run where some torrents failed shows *Finished with errors* and how many failed.

### Changed

- The default log level is `Information`. Set `FETCHSHELF_LOG=debug` to see every torrent each job looks at.
- **Refresh and relink** links the book again from the download in qBittorrent, into the library that the
  torrent's current category, save path and tags point to. It used to only move the existing folder.
- **Torrents** page: the search ignores case and accents, and only looks at titles, authors and series.
- Snatchlist runs read a torrent's details from the snatch list itself and only ask MAM about torrents
  Fetchshelf doesn't have yet, so long lists need far fewer requests.
- Only one job talks to MAM at a time, now including Download, Link's metadata lookups and applying a tag
  rule. A **Refresh** pressed while an autograb or snatchlist runs waits for it to finish.
- **Check qBittorrent** skips books recorded with `metadata_only_add`. Fetchshelf never downloaded them, so it
  can't look them up.
- **Events** and torrent pages show event types, library mismatches and changed fields in plain words.
- MAM failures on the **Search**, torrent and **Config** pages are explained in one sentence, with the details
  in the log. **Refresh** says when MAM doesn't have the torrent any more.
- **Remove from qBittorrent with data** asks you to confirm first.
- Configuration errors say how to fix them: `[audiobookshelf]` written as `[[audiobookshelf]]` and the like
  names the right form, and a misspelled autograb `type` or `cost` names the value. Startup stops with one
  log line instead of a stack trace.

### Fixed

- The Clean job treated two different books by the same author as copies of one book, and removed the one in
  the worse format from your library.
- Retiring a copy that shared its folder with the better copy deleted the better copy's `metadata.json` and
  `cover.jpg`. Use **Refresh and relink** on the kept torrent to write them again.
- Several `try_wedge` torrents in one download run could leave you with fewer wedges than `wedge_buffer`.
- Snatch lists longer than 250 rows: MAM sends 1000 rows per page, and Fetchshelf could read the same rows
  again up to `max_pages` times.
- `exclude_uploader` and the snatched-count settings were ignored on `[[snatchlist]]` blocks.
- One torrent that failed no longer ends a Download, Clean, snatchlist or Audiobookshelf run.
- A Link run where torrents could not be linked showed *Success*.
- After every image update, the log showed an error and forms left open in the browser were refused. The keys
  behind the forms are now kept in `/database/keys`.
- **Check qBittorrent** marked every book recorded with `metadata_only_add` as *not in client*. The next check
  clears that.
- **Refresh and relink** failed when the book's folder was gone, and did not move a book to its new library.
- The page shown after an unexpected error was ASP.NET's developer template.

### Removed

- Notion lists (`[[notion_list]]` and `import_interval`).

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
