# Fetchshelf releases

Every Fetchshelf release, newest first. Versions are `YYYY.MM.N`: the year and month of the release, and its
number within that month. Container images are published as `ghcr.io/crimsonking60/fetchshelf:<version>`.

Before upgrading, read the **Upgrading** notes of every release between your version and the new one.

## 2026.11.8 - 2026-11-13

Fetchshelf can follow series and authors and download what is missing, look for better copies of books already
in the library, and send notifications to ntfy, Gotify, Discord, Apprise or any webhook. A new **Account** page
shows the MAM numbers every safety rule works from, job runs are kept across restarts and can be cancelled,
failing jobs show on the **Errors** page, and the Search page pages through every result.

### Upgrading

- **Every scheduled job runs within minutes of the first start.** The job history starts empty, so every job
  counts as never run: the jobs start one after another, 30 seconds apart, instead of one interval after
  start. That includes the autograbs, the snatchlist, the Hardcover lists and the Download job. Set
  `dry_run` on a block first if it shouldn't download right away. From then on, a job's first run after a
  restart follows its last run.
- **Ebooks now prefer epub to cbz.** The default `ebook_types` starts with `epub` instead of `cbz`. If you don't
  set `ebook_types`, a torrent with both is now linked as epub, and the Clean job keeps an epub copy of a book
  over a cbz one. To keep the old order, set
  `ebook_types = ["cbz", "epub", "pdf", "mobi", "azw3", "azw", "cbr"]`; see
  [the format lists](https://github.com/crimsonking60/fetchshelf/blob/v2026.11.8/docs/configuration.md#top-level-settings).
- **An `[[autograb]]` with a `query` and no `sort_by`** (or `sort_by = "newest_first"`) now gets MAM's newest
  uploads first, instead of MAM's best matches. Set another `sort_by` if you relied on that order.
- The first start fills in the authors, narrators and series of every stored torrent, which takes a little
  while on a large library.
- Testers of the development image: `:develop` no longer updates. The head of development is now published as
  `:edge`.
- Everything new is opt-in: without `[follow]`, `[upgrade]` and `[[notify]]` nothing is followed, upgraded or
  sent. Otherwise existing configurations work unchanged.
- The database is upgraded when the new version starts, and going back to an older version is not possible.
  Back up your data folder first.

### Added

- **Follow series and authors** with a `[follow]` section. The new **Series** page lists every series in the
  library, with the books it has and those missing. A series or author can be set to **Follow** (what MAM has
  is listed on the new **Following** page, to download with a click), **Auto** (downloaded through the
  `[[follow.grab]]` blocks and the usual budget), or ignored to leave it out of the series with missing books.
  The Follow job reads every page of a follow's
  results the first time, then only what is new. Series and authors on MAM are entered by uploaders, so Auto
  can take box sets and companion books: start with Follow. See
  [`[follow]`](https://github.com/crimsonking60/fetchshelf/blob/v2026.11.8/docs/configuration.md#follow) (#49).
- **Upgrade to a better copy** with an `[upgrade]` section: the Upgrade job searches MAM for books linked in a
  format that isn't the first in their format list, such as an mp3 audiobook when m4b comes first, and selects a
  better copy of the same book. Without `[[upgrade.grab]]` blocks it only takes free copies. The **Torrents**
  page gets a *Could be upgraded* filter, and a torrent's page a **Look for a better copy** link. See
  [`[upgrade]`](https://github.com/crimsonking60/fetchshelf/blob/v2026.11.8/docs/configuration.md#upgrade) (#50).
- **Notifications** with `[[notify]]` blocks, to ntfy, Gotify, Discord, an Apprise API server or anything that
  takes a JSON POST: MAM refusing `mam_id` or rate limiting, a job that starts failing or works again, runs with
  errors, grabbed, linked and removed torrents, a new hit-and-run, and selections left waiting for budget. Bursts
  are merged into one message, and the **Config** page can send a test. With `web_url` set, messages link back
  to Fetchshelf. See [`[[notify]]`](https://github.com/crimsonking60/fetchshelf/blob/v2026.11.8/docs/configuration.md#notify) (#46).
- **Account** page: ratio, upload and download, unsatisfied snatches, wedges, bonus points and every kind of
  snatch MAM counts, with hit-and-runs first. Below that, the ratio budget, the selection slots and the wedges
  Fetchshelf works out from them, globally and per autograb, each with its formula, and when MAM last answered
  or why calls are paused. **Refresh from MAM** reads the account again. The **Jobs** page shows the main
  numbers (#44).
- Job runs are kept in the database: each job has a history page, a run says what it did in one line and what
  started it, and a running job can be cancelled from the **Jobs** page (#43).
- The **Errors** page lists jobs whose runs keep failing, with since when and **Run now**, and the items the
  snatchlist, Hardcover list and Audiobookshelf jobs couldn't handle. The **Errors** link shows a count (#45).
- **Torrents** page: filters for author, narrator and series, reached by clicking a name in the list or on a
  torrent's page (#47).
- **Search** page: pages through every result, 100 at a time, searches in chosen fields (title, author,
  series, narrator, description, tags, file names), sorts by best match, date, seeders, snatches, size or
  title, can be limited to freeleech, VIP, bookmarks or torrents not snatched yet, and can download into a
  chosen qBittorrent category (#48).
- `sort_by` on `[[autograb]]` also takes `high_seeders`, `high_snatches`, `largest_first` and `title` (#48).
- `extra_categories`: qBittorrent categories no library uses, offered in **Set category** and **Download
  into** for torrents that belong in no library.
- Saving the **Edit** page records what changed as an event marked as edited by hand.

### Changed

- A torrent downloaded by Fetchshelf is followed in qBittorrent on the server, and the Link job starts as soon
  as it has finished, instead of at the Link job's next run or only while its page was open.
- The Search page's **Metadata** column is now **Quality**, and also rates a torrent's formats against your
  format list.
- **Refresh** on a hand-edited torrent asks before replacing the edits with MAM's details, and the page says
  what **Refresh** and **Refresh and relink** each do.
- The **Events** page filters by grabber, linker and field with dropdowns, and has **Clear filters**.
- The Search page's filters sit right above the results, its **Search in** choices are always shown, and
  **Snatched** only marks results nothing else says were downloaded.
- The theme selector moved to a footer on every page, next to the Fetchshelf version.

### Fixed

- Cancelling or stopping a Download run couldn't cut short a MAM or qBittorrent call that hung, so a container
  stop could kill it in the middle of a torrent, after a wedge was spent. Such a call now stops within five
  seconds, and a torrent qBittorrent already has is still recorded (#43).
- A Hardcover list found nothing for an author whose initials MAM writes differently, such as "Sarah J.
  Maas" and "Sarah J Maas".
- A search, list or follow download that came across a torrent already in the library replaced details edited
  by hand with MAM's.
- Writes could fail with "database is locked" while a page or a job was reading the database.
- A torrent downloaded from the Search page was shown as *Not in qBittorrent* until qBittorrent's list was read
  again.
- A Hardcover list dry run reported 0 selected, and the Jobs page didn't show what it would have selected.
- An Audiobookshelf run that failed could report a failed save instead of what stopped it.
- An Audiobookshelf error stayed on the **Errors** page for good once its book was unlinked or replaced.
- While a job ran, the **Jobs** page showed the previous run's result next to *Running*.
- The **Events** page's field filter also matched values that merely contained the field's name.

## 2026.11.7 - 2026-11-10

A release of fixes to how Fetchshelf protects your account and your library. The ratio budget now really
keeps the ratio at `min_ratio`, files with the same name are no longer lost or deleted when linking, titles in
non-Latin scripts are no longer all taken for the same book, and a dropped connection to MAM no longer drops a
queued download. Every configuration value is checked at startup, and `--check-config` checks a file without
starting. `mark_removed` and `on_invalid_torrent` now work.

### Upgrading

- **Check your configuration before switching.** Startup now refuses values it used to accept or misread:
  intervals above 30 days or below 0, negative buffers and bounds, `max_pages` or `max_active_downloads` of 0,
  `min_ratio` of 0 or below, a non-numeric `ignore_torrents` entry, minimums above their maximums, filter values
  that were never valid (that block's job was already failing on every run), two blocks or two libraries with
  the same name, blocks named `Link`, `Clean`, `Download`, `Audiobookshelf`, `Manual search` or like
  `autograb[1]`, `mark_removed = true` on a `bookmarks` autograb, and an `on_cleaned`, `on_unlinked` or
  `on_invalid_torrent` table with neither a category nor tags. Run `--check-config` with the new image against
  your file first; see [Checking a configuration](https://github.com/crimsonking60/fetchshelf/blob/main/docs/configuration.md#checking-a-configuration).
- **Fewer ratio-cost downloads may start.** With `min_ratio` above 1, which includes the default of 2, the
  ratio budget used to allow downloads that took the ratio below `min_ratio`. Now an account at or below
  `min_ratio` starts none, and the rest wait on the **Selected** page until the ratio allows them. Free
  downloads are unaffected. The Download job logs the budget and what it was worked out from at the start of
  every run.
- **`download_dir` and `path_mapping` match whole folder names.** `download_dir = "/data/audio"` no longer
  claims torrents in `/data/audiobooks`, and a mapping for `/downloads` no longer rewrites `/downloads2`. If you
  relied on that, write the full folder name. Torrents already linked stay linked; one no library claims any
  more is flagged with a library mismatch on the **Torrents** page.
- **The next Clean run may retire more copies.** Author and narrator names are now compared loosely
  ("J. R. R. Tolkien" and "J.R.R. Tolkien" are one author), so copies of a book that weren't recognized as
  duplicates before can be now.
- The first start recomputes every stored title key, which takes a few seconds on a large library.
- If you run two Fetchshelf instances on one host, each now names its form cookies after itself. Reload any
  page left open from before the upgrade before using its forms.
- The database is upgraded when the new version starts, and going back to an older version is not possible.
  Back up your data folder first.

### Added

- `--check-config`: loads the configuration as startup would and prints a summary of the safety settings,
  qBittorrent connections, libraries and jobs, or says why Fetchshelf would not start. It contacts nothing and
  doesn't open the database (#33).
- `mark_removed` on `[[autograb]]` now works: stored torrents missing from a run's results are looked up on
  MAM, up to 20 a run and each at most once a week, and those MAM no longer has are marked as removed from MAM.
  They show under *Problems* on the **Torrents** page and with a notice on their page. Nothing is changed in
  qBittorrent or the library (#39).
- `on_invalid_torrent` on `[[qbittorrent]]` now works: a torrent the Link job finds on MAM but can't read,
  for example because of a new media type, gets the category and tags you set. A category no library uses
  stops it being looked up on every run (#39).
- **Errors** page: **Edit** on a torrent that couldn't be linked because MAM gives it no author. Saving with an
  author added records it, and the next Link run links it (#30, #31).
- A torrent without an author on MAM whose title ends in "by" and a name, such as "Exploitation Film Posters by
  Graham Marsh", gets that name as its author (#31).
- `fs`, a read-only command-line view of a Fetchshelf database, in `utils/fs`. See [docs/fs.md](https://github.com/crimsonking60/fetchshelf/blob/main/docs/fs.md).
- The web UI has the Fetchshelf logo as its icon.

### Changed

- A request to MAM that gets no answer, or a 502, 503 or 504, is sent once more after 2 seconds. Downloads that
  spend a wedge are not repeated (#34).
- MAM requests carry the running Fetchshelf version in their user agent.

### Fixed

- The ratio budget let downloads take the account below `min_ratio` whenever `min_ratio` was above 1 (#32).
- Two files with the same name in one torrent, such as `Part 1/01.mp3` and `Part 2/01.mp3`, were linked as one,
  and the second was missing from the library without an error. Clashing names now get their sub-folder in
  front, or a number. **Relink** on a torrent's page brings back files lost this way (#38).
- Two torrents of one book sharing a folder, each with a file of the same name, recorded the same file, and
  retiring or relinking one of them deleted the other's file. A different file already in place is now kept,
  and the new one goes under a numbered name (#38).
- An interrupted copy left a partial file that was taken for the finished one on the next run (#38).
- Titles written entirely in Cyrillic, Greek, Arabic, Hebrew, Chinese, Japanese or Korean were all taken for
  the same book, so a second book by an author could be skipped as a duplicate or retired from the library by
  the Clean job. Latin letters such as "Æ" were dropped from title comparisons too (#36).
- A dropped connection, a timeout or a server error while fetching a torrent file took the selection out of
  the queue as a stale link. It now stays queued for the next run. A link that really is stale is refreshed,
  and a torrent MAM no longer has is taken out of the queue with that reason (#34).
- Two blocks with the same name, or a block named like a built-in job, silently replaced each other's job;
  an autograb named `Download` stopped all downloads (#35).
- `download_dir` and `path_mapping` matched partial folder names (#37).
- A configuration mistake in a number, a filter or an environment override showed up only when a job ran, or
  stopped startup with a stack trace. Each is now refused at startup with a message naming the setting (#33).
- Saving **Edit** dropped the description, ISBN, file types, size and other details the form doesn't show
  (#31).
- With two Fetchshelf instances on one host, using one made the other's forms fail with an empty HTTP 400, and
  its page messages went missing.

## 2026.11.6 - 2026-11-09

Fixes for the OPDS catalog: book descriptions show as text instead of HTML tags, a stray character in a MAM
description no longer breaks a feed, and each library gets its own recently added, authors and series views.
`[audiobookshelf.path_mapping]` now reads the same way round as `[qbittorrent.path_mapping]`.

### Upgrading

- If you set up `[audiobookshelf.path_mapping]` in 2026.11.5, swap the two sides of every line: the
  Audiobookshelf path now goes on the left and Fetchshelf's path on the right, as in
  `[qbittorrent.path_mapping]`. Fetchshelf refuses to start with a table that looks written the old way round
  and says to swap it.
- Nothing else to do. There is no database migration. Going back to 2026.11.5 is possible, after swapping the
  `[audiobookshelf.path_mapping]` lines back.

### Changed

- `[audiobookshelf.path_mapping]` maps Audiobookshelf's path = Fetchshelf's path, like
  `[qbittorrent.path_mapping]`. Mapping two Audiobookshelf paths to the same Fetchshelf path stops startup
  with an error.
- OPDS: a library under **Libraries** opens its own start feed with **Recently added**, **Authors** and
  **Series**, instead of one long list of all its books. Search still covers every served library.

### Fixed

- OPDS: book descriptions showed MAM's HTML tags in readers. They are now sent as plain text.
- OPDS: a character XML can't hold, such as a form feed in a MAM description, made every feed listing that
  book fail. Such characters are now left out, and also no longer break `metadata.opf` for that book.

## 2026.11.5 - 2026-11-08

Fetchshelf can serve ebook libraries as an OPDS catalog for e-readers and reading apps, and the Search page
follows a download all the way into the library, rates each result's metadata and says up front when a torrent
can't be downloaded. A top-level `languages` setting sets the language once for every autograb and Hardcover
list.

### Upgrading

- Nothing to do. There is no database migration, and the new settings default to the earlier behaviour, so
  existing configurations work unchanged. Going back to 2026.11.4 is possible.

### Added

- `[opds]`: serves your ebook libraries as an OPDS catalog at `/opds`, for KOReader, Moon+ Reader, Thorium and
  other readers, with recently added books, authors, series, search, covers and downloads. Audiobooks are never
  served. The **Config** page shows the catalog's address (#20, #21).
- `languages` at the top of the file: the languages of every `[[autograb]]` and `[[hardcover_list.grab]]` block
  that doesn't set its own. `[[tag]]` rules don't use it.
- Search page: a language list next to the search box, starting at the top-level `languages`.
- Search page: a **Metadata** pill rates each result's information on MAM as Good, Lacking or Poor; point at it
  to see what is missing.
- Search page: **Can't download** replaces the download buttons on a result whose file types are unknown or not
  in your format list, with the reason.
- Search page: a **Downloading** status, between **Selected** and **Downloaded**.
- `path_mapping` in `[audiobookshelf]`: maps a path as Fetchshelf sees it to the same location as
  Audiobookshelf sees it, so Audiobookshelf can mount just the library, at a path of its own, instead of at
  Fetchshelf's path.

### Changed

- Search page: a **Download** click answers at once, then shows whether the torrent was added to qBittorrent.
  A result's status then updates by itself without a reload, and once qBittorrent has finished a torrent the
  Link job starts straight away, so **Linked** follows within seconds.
- A selection made while the Download job is running gets a run straight after it, instead of waiting for the
  next scheduled run. Autograbs and Hardcover lists use the same follow-up run.
- A misspelled name in the filter of an `[[autograb]]`, `[[snatchlist]]` or `[[hardcover_list.grab]]` no longer
  stops Fetchshelf from starting; that job fails on each run instead, and the **Jobs** page says why. Mistakes
  in `[[tag]]` rules and the top-level `languages` still stop startup.

### Fixed

- A Hardcover list's `free` grab block took non-free torrents too, then rejected the book as too expensive
  without trying the list's later blocks. Each block now only takes torrents its `cost` accepts and leaves the
  rest to the next block.

## 2026.11.4 - 2026-11-07

Fetchshelf now handles a library of 10,000 torrents comfortably: the Link and Clean jobs no longer slow down
with the square of the library's size, and the Link job asks MAM far less. Libraries can get a Calibre-style
`metadata.opf` next to each book, and a Hardcover list can send its downloads to a qBittorrent category of its
own.

### Upgrading

- Nothing to do. There is no database migration, and every new setting defaults to the earlier behaviour, so
  existing configurations work unchanged. Going back to 2026.11.3 is possible.

### Added

- `metadata.opf`: with `metadata_opf = true`, a library gets a `metadata.opf` next to each book, the OPF file
  Calibre keeps next to its books and imports from. Audiobookshelf reads it too. `metadata_json = false` leaves
  out `metadata.json`. Both can be set on a `[[library]]`, or at the top of the file for every library that
  doesn't set its own. Changing them affects books linked from then on; **Relink selected** applies them to
  books already in the library (#16).
- `category` on a `[[hardcover_list]]`: the qBittorrent category for everything the list downloads, as an
  autograb's `category`. It overrides `[[tag]]` rules, so different lists can go to different libraries.
- `database_wal`: keeps the web interface responsive while a long job writes to the database, using SQLite's
  write-ahead log. Off by default; turn it on only when the database is on a local disk, not a network share
  (#13).

### Changed

- **Large libraries.** At 10,000 torrents, a Link run with nothing to do took about two and a half minutes
  and now takes a few seconds; a Clean run went from over a minute to under a second. Pages and actions that
  need one torrent from qBittorrent, such as a torrent's page, no longer fetch every torrent qBittorrent has
  (#13, #14).
- The Link job only stores a torrent's qBittorrent status when it moves to another stage, such as from
  seeding to stopped, not each time qBittorrent flips between *uploading* and *stalled*. The **Torrents** list
  no longer shows qBittorrent's raw state when you point at the status; a torrent's page still shows it (#14).
- A torrent MAM had no torrent for (*MAM has no torrent with this info-hash* on the **Errors** page) is looked
  up again once a day instead of on every Link run. Remove its entry on the **Errors** page to have the next
  run look it up straight away (#15).
- A Link run looks up at most 200 unknown torrents on MAM. Pointed at a qBittorrent that already holds
  thousands of books, Fetchshelf works through them over several runs while books it knows keep being linked
  (#15).
- With `download_after_selection = false`, the Search page's buttons say **Select** and **Wedge select**,
  since they only queue the torrent on the **Selected** page.

### Fixed

- A search result's details showed no cover. They now show MAM's cover image.
- A crafted request for a search result's cover could add made-up lines to the log.

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
- **Search** page: a result's title opens everything MAM returns for it, over the results: cover, narrators,
  series, language, categories, flags, tags, ISBN or ASIN, files, uploader, seeders, cost, the description, the
  media info (duration, format, bitrate and more, where MAM has it) and every raw field, and whether Fetchshelf
  and qBittorrent have it.

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
