# Fetchshelf releases

Every Fetchshelf release, newest first. Versions are `YYYY.MM.N`: the year and month of the release, and its
number within that month. Container images are published as `ghcr.io/crimsonking60/fetchshelf:<version>`.

Before upgrading, read the **Upgrading** notes of every release between your version and the new one.

## 2026.11.22 - 2026-11-22

Libraries can now be chosen by **genre**: with `genres = true`, a `[[library]]` takes `allow_genres`, `deny_genres` and
`fiction`, and the Torrents, Books and Search pages get Fiction and Genre filters. **Covers** no longer come from MAM,
whose image server started refusing Fetchshelf: they come from the books' own files, then from Hardcover, Open Library
and Audnexus, and `covers_from_mam` is renamed `covers`. The Library check's **Delete selected** and **Relink
selected** run in the background, and Relink says when qBittorrent can't find a torrent's files.

### Upgrading

- The database is upgraded when the new version starts (a table for each torrent's genres is added and filled, about
  6 seconds for 12,000 torrents), and going back to an older version is not possible. With a
  [`[backup]`](https://fetchshelf.readthedocs.io/en/v2026.11.22/configuration/#backup) section, a backup is taken first, named
  `data-<time>-2026.11.21-before-2026.11.22.db`; without one, back up the data folder first.
- **`covers_from_mam` is now `covers`.** The old name still works, with a warning at start-up to rename it; setting both
  is an error. Every linked book is looked at again for a cover once, so the first Link runs after the upgrade write
  more covers than usual, and the Link job's line says how many are left.
- With a large `covers` and many books without a cover, a Link run can take up to about 5 minutes longer, as Open
  Library allows one request every 3 seconds.
- Nothing else is needed: `genres` and `[search] covers` are off by default.

### Added

- **Libraries by genre**: `genres = true` (default `false`) and, in a `[[library]]`, `allow_genres`, `deny_genres` and
  `fiction` (`"fiction"` or `"nonfiction"`), by MAM's genre names. The Link job puts a book in the first library its
  category, folder and tags give it whose genre settings take it; a book no library takes is left unlinked, with the
  reason on the Errors page, and linked once the configuration takes it. **Relink** and the Library check use the same
  choice, and a linked book whose genres now pick another library shows *Different library*. See
  [Sorting books by genre](https://fetchshelf.readthedocs.io/en/v2026.11.22/user-guide/#sorting-books-by-genre) and
  [Libraries by genre](https://fetchshelf.readthedocs.io/en/v2026.11.22/configuration/#libraries-by-genre) (#178, #179).
- **Genre filters** (with `genres = true`): **Fiction** and **Genre** on the Torrents and Books pages, and on the Search
  page, where they hide results of the page MAM sent, as MAM's search can't filter by genre. A torrent's page and a
  search result's details show its main category and genres by MAM's names (#180, #181).
- **Editing genres** (with `genres = true`): the Edit page sets a book's main category and genres. `metadata.json`,
  `metadata.opf`, Audiobookshelf, the OPDS catalog and the Books page get MAM's genre names, such as
  *Politics/Sociology*, plus *Fiction* or *Nonfiction*; files already written change at their next write (#182).
- **Covers from the books' own files**: the Link job reads the cover inside a book's EPUB, comic, m4b, m4a or mp3 and
  writes it into the folder, spending at most about 10 seconds a run on reading (#186).
- **Covers from Hardcover, Open Library and Audnexus**, at most `covers` a run, for books whose files have none:
  Hardcover (with the `[hardcover]` token) by ISBN, ASIN, or title and author; Open Library by ISBN; Audnexus by an
  audiobook's ASIN. Only those details are sent. A service that refuses is skipped for the rest of the run. The Link
  job's line says where the covers came from, such as "12 covers written (8 from the books' files, 3 from Open
  Library, 1 from Audnexus)". See [Covers](https://fetchshelf.readthedocs.io/en/v2026.11.22/user-guide/#covers) (#187, #189, #191).
- **`[search] covers`** (default `false`): a cover in a search result's details, from the same sources, remembered for
  an hour (#185, #189, #190).

### Changed

- **No covers from MAM.** MAM's image server answers Fetchshelf with 403 "Agent Miss-Match" since 2026-11-22, and was
  never among the parts of MAM its API documentation allows automating. Nothing asks it any more, on the Link job or
  the Search page (#185).
- **Library check**: **Delete selected** and **Relink selected** run in the background like the Torrents page's bulk
  actions, with progress and **Cancel**, one finding at a time, so the Link and Clean jobs no longer wait for the
  whole run. **Run the check** waits until they are done (#184).
- **Books page**: a card shows at most three authors, with **... more** opening the book (#177).

### Fixed

- **Relink** of a torrent whose files were moved or deleted outside qBittorrent said "It has not finished
  downloading". It now says that qBittorrent can't find its files, names the folder it looks in, and how to bring
  them back; the Library check and the Search page's Import say the same (#188).
- **Delete selected** on the Library check with more than about 1,000 findings ticked answered HTTP 400. Forms now
  take up to 100,000 values (#183).
- Genres 26 and 48 were written to files as *Lick Library Ltp* and *Sheet Collection Mp3*; they are now *Erotica* and
  *Accompanying Music*, and genre 63 is *Military*, at each book's next write (#178).
- The Event log records a change of a book's main category or genres (#182).
- The book dialog on the Books page no longer shows a space before the comma between authors or series (#177).

## 2026.11.21 - 2026-11-22

A new **MAM API** page, under **Insight**, records every call Fetchshelf makes to MAM and charts them, to see the
pattern behind a 429 (too many requests). What it showed led to the next change: `.torrent` downloads are now at
least two seconds apart, as MAM refused the eleventh of a run of downloads one second apart. Follow finds that reach
the library another way, such as through another follow, now leave **To look at** on the next Follow run, and the
Link job's count of books still waiting for a cover from MAM is exact. The user guide shows screenshots of the pages it
describes.

### Upgrading

- The database is upgraded when the new version starts (a table for the calls to MAM is added), and going back to an
  older version is not possible. With a [`[backup]`](https://fetchshelf.readthedocs.io/en/v2026.11.21/configuration/#backup)
  section, a backup is taken first, named `data-<time>-2026.11.20-before-2026.11.21.db`; without one, back up your
  data folder yourself.
- The calls to MAM are kept for 30 days, a few hundred to a few thousand rows a day.
- Nothing else is needed: no setting is added or changed.

### Added

- **MAM API**: every request sent to MAM is recorded with when it started, how long it took, what MAM answered (or
  why there was no answer) and the job that made it. The page charts the calls per 1, 10 or 30 minutes over 1, 6 or
  24 hours, with each 4xx or 5xx answer as a red line and details on pointing at a bar or line; arrows move the
  window back and forward. Below the chart are the window's statistics (429s, the most calls in any one and any ten
  minutes, the shortest gap, the time to answer, and the calls by endpoint and by job) and the calls themselves,
  newest first. The latest window refreshes itself. The page is in the menu only when `mam_id` is set. See
  [MAM API](https://fetchshelf.readthedocs.io/en/v2026.11.21/user-guide/#mam-api) (#172).

### Changed

- **Downloads**: at least two seconds between two `.torrent` downloads from MAM, up from one. A Download run of 15
  torrents takes about 15 seconds longer (#173).

### Fixed

- **Following**: a find whose book reached the library or the download queue by another route, such as another
  follow on *Auto*, an autograb or the Search page, stayed under **To look at** until its own follow was searched
  again, up to a day later, and was counted in the menu's badge meanwhile. Every Follow run now checks the waiting
  finds of all follows, without calling MAM (#174).
- **Covers**: with `covers_from_mam`, the Link job's log said every linked book not looked at yet would get a cover in
  later runs, counting books in series-layout libraries and books that already have an image. The count now holds
  only the books without an image, and a run that reaches its limit still marks the rest as looked at (#171).

### Documentation

- The [user guide](https://fetchshelf.readthedocs.io/en/v2026.11.21/user-guide/) has screenshots of twelve pages
  where it describes them, among them Books, Search, Following, Account, MAM API and Jobs. The books, covers and
  account in them are made up (#175).

## 2026.11.20 - 2026-11-21

A new **Books** page, first in the **Library** menu, shows every book Fetchshelf has linked, audiobooks included, as
a grid of covers to browse, search and open in Audiobookshelf. Its books' files, audiobooks included, can be
downloaded from it when the configuration has an `[opds]` section, behind the OPDS login when one is set; the OPDS
catalog itself still serves ebooks only. Covers now also come from each book's folder and from m4b, mp3 and
CBR files, which gives the OPDS catalog covers for PDFs and comics as well. The documentation was checked against the
code, and where they disagreed, either was put right.

### Upgrading

- Nothing is needed for the database: it is unchanged, so going back to 2026.11.19 is possible.
- **Some configurations are now refused at start-up.** `[audiobookshelf]` needs both `url` and `token`, every
  `[[library]]` needs `library_dir`, two `[[hardcover_list]]` blocks can't read the same `status` or `list`, and
  `media_type` takes only names such as `"Ebook"`. Run `--check-config` against the new version before switching
  over; an error names the section it is in.
- **Log files are limited.** With `FETCHSHELF_LOG_DIR` set, a log file now rolls on to a new one at 100 MB, and the
  newest 31 files are kept. An unknown `FETCHSHELF_LOG` value is warned about at start-up.
- **The `covers` folder** next to the database is used without an `[opds]` section too, for the **Books** page, and
  the Clean job removes the covers of books no longer in the library once a week.
- **`metadata.json` language names** are written as shown (`Brazilian Portuguese`, not `BrazilianPortuguese`). Files
  already written change at their book's next relink.
- `--update-search-title`, which was never documented, is removed; started with it, Fetchshelf ignores it and runs as
  usual.
- No setting is added or renamed.

### Added

- **Books**: a page of every linked book, audiobooks included, as a grid of covers: **Recently added**, **Authors**,
  **Series** and **Libraries** (one column, A-Z), a search over title, author and series, and an **All / Audiobooks /
  Ebooks** filter. A book opens with its details, narrators, description and files, **Open in Audiobookshelf** when
  it is matched, and **Details** for its torrent page. A series links to the **Series** page for its missing books.
  `g` then `b` opens it. See [Browsing your books](https://fetchshelf.readthedocs.io/en/v2026.11.20/user-guide/#browsing-your-books) (#162, #163, #166, #169).
- **Books**: each file, audiobooks included, downloads from the page with a `Title - Author` name and resumable
  ranges; an audiobook in several files also downloads as one zip, with its `Disc N` folders, and one in a single file
  has a preview in the browser's player. Downloads are offered only when the configuration has an `[opds]` section,
  which still serves only ebooks to e-readers; with an `[opds]` login, the page's downloads and covers ask for it,
  while the page itself doesn't (#163, #164).
- **Covers**: a book's cover is the image in its own folder (`cover.*`, `folder.*` or its only image), such as the one
  `covers_from_mam` writes, else the cover inside the file: EPUB and CBZ as before, and now m4b, m4a, mp3 and CBR. In
  a series-layout library, where a series' books share a folder, only covers inside the files are used. The OPDS
  catalog gets the same covers (#165).
- **Clean** removes, once a week, the cached covers of books no longer in the library (#167).
- **Jobs**: Backup, Bonus, Follow and Upgrade are listed as not configured when they aren't (#158).
- **Config**: the `[audiobookshelf]` and `[hardcover]` sections, with their tokens hidden, and
  `download_after_selection` (#158).

### Changed

- **Wedges**: a selection to be fetched with a wedge is checked on MAM just before the wedge is spent, and downloaded
  without one if it has turned freeleech or VIP meanwhile. A `wedge` selection waiting at `wedge_buffer` makes no
  request, as before (#153).
- **Lists**: with `prefer_format`, once the preferred format is selected or in the library, the other format is no
  longer searched for; it is wanted again if the preferred one leaves the queue or the library (#153).
- **Torrents**: **Refresh selected** skips torrents edited by hand and lists them under the operation's problems; a
  torrent's own **Refresh** still replaces the edits after asking (#157).
- **Library check**: always in the **Library** menu, with a count when the last check found something, and always
  linked from its job on the **Jobs** page. **Delete selected** asks for confirmation first (#158, #168).
- **OPDS**: a book split into several files lists its parts in number order (`Part 2` before `Part 10`) (#162).
- `--check-config` lists Library check, Seeding, Backup, Follow, Upgrade and Bonus with the other jobs (#155).
- Configuration errors name the section they are in, such as `the [[library]] 'Audiobooks'` (#155).

### Fixed

- **Lists**: a book on a list without an author was never found: the search asked MAM for an empty author and the
  match required one. It is now searched for and matched on its title alone (#156).
- **Split collections**: a hand edit of a collection's book was shown on the Torrents page, but the book's folder kept
  following MAM's details; **Refresh and relink** now moves it to match the edit (#157).
- **Config page**: **Run now** showed an unstyled fragment instead of the Jobs page (#158).
- A list page said a missing format is searched for once a day; it is daily in a book's first week on the list, then
  weekly (#156).
- `utils/fs` knew only some event kinds and error stages, and its `--state` and `--stage` filters didn't ignore case
  as documented (#160).
- The user, developer and maintainer documentation was corrected wherever it no longer matched the code, and the
  example configurations work as their comments say (#152, #154).

## 2026.11.19 - 2026-11-20

A Link run over a large library takes seconds instead of a minute. The **Torrents** page keeps **Check qBittorrent**,
the export links and **Per page** together above the list when the filters are open. The documentation has a new
[Getting started](https://fetchshelf.readthedocs.io/en/v2026.11.19/getting-started/) guide that takes a new user from
installing Fetchshelf to a first download.

### Upgrading

- The database is upgraded when the new version starts (one index is added), and going back to an older version is
  not possible. With a [`[backup]`](https://fetchshelf.readthedocs.io/en/v2026.11.19/configuration/#backup) section, a
  backup is taken first, named `data-<time>-2026.11.18-before-2026.11.19.db`; without one, back up your data folder
  yourself.
- Nothing else is needed: no setting is added or changed.

### Added

- [Getting started](https://fetchshelf.readthedocs.io/en/v2026.11.19/getting-started/): a step-by-step guide for new
  users, from planning the folders and installing with Docker Compose to a first `config.toml`, a dry run and a first
  download, ending with a tour of what else Fetchshelf can do (#150).

### Fixed

- **Link**: a run with nothing to do took about a minute on a library of a few thousand torrents, and longer the larger
  the library. Each torrent's check for another language's edition in the same folder read the whole torrent list;
  it now finds it through an index (#149).
- **Torrents**: with the filters open on a wide screen and a `[seeding]` section, **Check qBittorrent** stayed on the
  filters' line while the export links and **Per page** dropped to the next line at the left. The three now move as
  one group, at the right just above the list (#148).

## 2026.11.18 - 2026-11-20

A followed series' or author's page shows the whole of it: the books already in the library are listed as *Already
owned*, a find waiting since before the library had its book catches up, and each book and format is listed once. A
series' page lists its books by number. The **Torrents** page has **Per page** above the list too, and on a phone a
table's column names stay in view.

### Upgrading

- Nothing is needed. The database is unchanged, so going back to 2026.11.17 is possible, and no setting is added or
  changed.
- **Follows fill in at their next search.** Each follow's page is brought up to date the next time the Follow job
  searches it: the books already had are added, waiting finds the library now covers become *Already owned*, and
  extra listings of one book and format left by earlier versions are removed, keeping the most settled one. Dismissed
  finds are kept. No extra MAM calls are made.
- **A series' page opens sorted by book number.** Links and bookmarks without a sort in them get the new order; the
  **Found** heading still sorts newest first.

### Added

- **Following**: a follow's page lists the books the library already has as *Already owned*, and the ones selected as
  *Selected*, so its counts cover the whole series or author, not only what was missing when it was first searched
  (#142).
- **Torrents**: **Per page** is also above the list, to the right of the export links (#145).

### Changed

- **Following**: a series' page lists its finds by book number, with the **Book** column first. The lists of finds
  and the list of follows have the sortable headings of the other pages, with an arrow for the column sorted by and
  its direction (#146).
- **Torrents**: **Check qBittorrent**, the export links and **Per page** sit on the line just above the list, also
  when the filters are open on a wide screen (#145).

### Fixed

- **Following**: a find waiting since before the library had its book stayed on offer until a search returned that
  torrent again. Waiting finds are now checked against the library at every search, and a newer upload of a book
  already listed is the same find, not a second one. A find without a language counts as the first of `languages`
  (#143).
- On a phone, a table's column names were drawn a header's height down inside the table and scrolled away with it.
  They now stay under the page's header while the table scrolls past (#145).

## 2026.11.17 - 2026-11-20

The Link job no longer makes other jobs fail with "database is locked" while it runs. A bulk action on the
**Torrents** page keeps the ticked rows and the place in the list, so its progress stays in view further down. The account's
numbers are read from MAM when Fetchshelf starts, each Upgrade run is kept as an event, and `[[autograb]]` blocks can
carry a description of their own.

### Upgrading

- The database is upgraded when the new version starts, and going back to an older version is not possible. With
  a [`[backup]`](https://fetchshelf.readthedocs.io/en/v2026.11.17/configuration/#backup) section, a backup is taken first, named
  `data-<time>-2026.11.16-before-2026.11.17.db`; without one, back up your data folder yourself.
- **Two pages open on another list.** The **Series** page opens on the new **Not ignored** choice, and a series' or
  author's page on the **Following** pages opens on **All** instead of **To look at**. Links and bookmarks without a
  filter in them get the new default; **All series** and **To look at** are still in the dropdowns.
- **One more MAM call per start.** With a `mam_id`, Fetchshelf reads the account from MAM once when it starts, unless a
  job has read it first.
- Nothing else is needed: no setting is added or changed except the new, optional `description`.

### Added

- `description` on `[[autograb]]`: a line shown under the block's job on the **Jobs** page and under its name on the
  **Config** page (#136). See [`[[autograb]]`](https://fetchshelf.readthedocs.io/en/v2026.11.17/configuration/#autograb).
- Each run of the Upgrade job is an *Upgrade run* event on the **Events** page, with its result as the **Jobs** page
  shows it (#138).
- The account is read from MAM when Fetchshelf starts, so the **Jobs** and **Account** pages have its numbers from the
  start instead of saying none have been read (#138).
- **Library check**: with more than one library, a dropdown shows one library's findings, and the buttons come back to
  it (#136).
- On a torrent's page, the actions are a column of buttons, each with what it does beside it; **Split into its books**
  sits next to **Refresh and relink**, with a short explanation (#136).
- The **Torrents** page's **Filters** panel opens as it was left on the last visit (#136).

### Changed

- **Series**: **Not ignored** is the first choice and the default; **Every series** is **All series** (#136).
- **Following**: a series' or author's page opens on **All**; and a find of a book that is already had, by this
  torrent or another copy of it, says *Already owned* instead of *In the library* (#138, #140).
- **Torrents**: a bulk action keeps the ticked rows and where the list was scrolled to. While the progress notice at the
  top is scrolled out of view, the strip at the bottom of the window shows the progress instead; it is as wide as the
  table (#137).
- The theme menu's button is a clearer icon (#136).

### Fixed

- The Link job took the database's write lock back the moment it committed each batch, so another job saving during a
  Link run could wait out its 30 second timeout and fail with "database is locked". It now leaves the lock free between
  batches (#139).
- **Torrents**: the Search button no longer drops below the search box when the filters are folded, and the box keeps
  its width (#136).
- **Search**: the **Download** and **Wedge download** buttons stay on one line (#136).
- **Config**: an autograb block's type was shown as `Fetchshelf.Data.Configuration.AutograbType` (#136).

## 2026.11.16 - 2026-11-20

The web interface is easier to find your way around and fits narrower screens. Related pages share a menu, tables
have fewer and wider columns with sortable headings and headings that stay in view, and the Jobs page groups its
jobs. Running jobs and bulk actions stay in sight from anywhere on a page, and destructive actions ask in a dialog
first. The Link job no longer holds the database long enough to make a list job fail.

### Upgrading

- Nothing is needed. The database is unchanged, so going back to 2026.11.15 is possible, and no setting is added or
  changed.
- **The theme menu has moved** from the bottom of every page to the ◐ button at the right of the menu bar. A browser
  that never chose a theme now gets **Match system**: Dark Warm while the device is set to dark, Light Warm while it
  is set to light. A theme chosen before is kept. See
  [Using the web interface](https://fetchshelf.readthedocs.io/en/v2026.11.16/user-guide/#using-the-web-interface).

### Added

- Menus: **Search**, **Torrents**, **Selected**, **Jobs** and **Errors** stay on the bar, the other pages sit under
  **Library** (Series, Following, Lists, Duplicates, Library check) and **Insight** (Events, Statistics), with the
  counts of their pages on the menu's button (#115).
- A theme menu in the header, with **Match system** as the default (#124).
- The **Jobs** page groups its jobs: the standard jobs, those that come with a section of the configuration, and one
  per `[[autograb]]` in the order of the configuration (#116).
- Sortable headings on **Torrents**, **Series**, **Events**, **Errors** and **Selected** (#114, #119), and a pager
  with numbered pages on every paged list (#118).
- On **Torrents**, **Selected**, **Errors** and **Duplicates**, the bulk-action bar shows while rows are ticked, says
  how many, and stays at the bottom of the window (#121).
- A pill in the menu bar names the jobs running, on every page, and **Run now** answers at once (#129). While a bulk
  action runs, a strip at the bottom of the **Torrents** page shows its progress and links to it (#133).
- Keyboard shortcuts: `/` for the search box of Search, Torrents and Series, `g` then a letter for a page, `?` for the
  list (#130).
- Destructive actions ask in a dialog that says what and how many, such as "Remove the records of 3 selected torrents
  from Fetchshelf?". The bulk **Remove selected** on Selected, Errors and Duplicates now asks too (#131).

### Changed

- Fewer, richer table columns: on **Torrents** the authors and series sit under the title and an **Added** column is
  new, and Search, Selected, Series, Events and Errors are narrower too; titles wrap between words, not inside them
  (#114, #117). The **Torrents** filters fold away, with the ones in use shown as chips (#117).
- Table headings stay under the menu bar while a table scrolls (#127), and times in tables are short ("3 h ago",
  "yesterday 21:14"), with the exact local time on hover (#126).
- Each page's explanation is shown in full on the first visit and folds to one line after that (#125).
- An empty list says why it is empty and what to do next (#128).
- One main button per part of a page, such as **Search**, **Save** or **Refresh and relink**, and on the
  **Torrents** bulk-action bar, **Unlink selected** and **Remove selected** sit apart at its right (#131, #132).
- Stronger row stripes and clearer panels in the themes, warning colors set apart from links, and every theme's
  contrast checked by a test (#113). The **Config** page's tables fit the window (#123).

### Fixed

- The Link job kept one database transaction open for up to a minute on a large library, so a list job saving at the
  same time could fail with "database is locked". A batch now closes after 2 seconds (#122).
- The **Added** date on **Torrents** was the UTC date, and its hover the UTC time; both are local now (#126).
- The red count on the menu was hard to read in the dark themes, and Light Warm's accent lines, such as the current
  page's underline, were too pale (#113).

## 2026.11.15 - 2026-11-19

A collection torrent, such as all of Discworld or a run of comic issues, can be linked as one library book each. A
new Library check finds library folders and records that don't match. A Statistics page shows the library and what
was grabbed, with an optional Prometheus endpoint, and the Link job can fetch covers from MAM for books without one.
The Bonus job buys upload credit only in the amounts MAM allows automations to buy.

### Upgrading

- The database is upgraded when the new version starts, and going back to an older version is not possible. With
  a [`[backup]`](https://fetchshelf.readthedocs.io/en/v2026.11.15/configuration/#backup) section, a backup is taken first, named
  `data-<time>-2026.11.14-before-2026.11.15.db`; without one, back up your data folder yourself.
- **Check `upload_amounts` in `[bonus]` before upgrading.** It may now hold only 50 and 100, the amounts MAM's rules
  allow automated purchases of; startup stops with an error naming any other amount, such as 1, 5, 20 or 25. Without
  `upload_amounts`, the default is now `[100, 50]`, so points below 25,000 above `keep_points` are left unspent. See
  [`[bonus]`](https://fetchshelf.readthedocs.io/en/v2026.11.15/configuration/#bonus).
- **Nothing is split until asked for.** `split_collections` is off unless set, and collections linked before stay as
  one book until they are relinked with it on. Splitting goes by MAM's details and the file and folder names, whose
  conventions vary widely: look over a collection's books after it is first split. See
  [Collections](https://fetchshelf.readthedocs.io/en/v2026.11.15/user-guide/#collections).
- Nothing else is needed: covers from MAM and the metrics endpoint are off unless set, and the Library check runs only
  when started.

### Added

- `split_collections`, at the top level as every library's default and on a `[[library]]`, links a collection of
  ebooks, comics, manga or periodicals as one book each, with its own folder (or file in a series library), metadata,
  cover and record (#105). A collection is found from MAM's series range or from numbered file names; each book takes
  its own format, and its title, series and number from its epub or its name. A torrent's page has **Split into its
  books** to choose for that torrent. The **Torrents** page shows a collection as one row, with a *Books of
  collections* filter to list its books. See [Collections](https://fetchshelf.readthedocs.io/en/v2026.11.15/user-guide/#collections).
- The **Library check** job and page list library folders and files no record knows of, and records whose folder or
  files are gone, each with what to do about it and buttons to do it (#103). On the **Jobs** page, its **Findings**
  link follows **Run now** when there are findings. See [Checking the library](https://fetchshelf.readthedocs.io/en/v2026.11.15/user-guide/#checking-the-library).
- A **Statistics** page: the library's totals by media type, language, library and format, what was grabbed by month,
  cost and grabber, and each job's runs over the last 30 days (#102). `metrics = true` serves the same as Prometheus metrics at `/metrics`. See
  [Statistics](https://fetchshelf.readthedocs.io/en/v2026.11.15/user-guide/#statistics) and [Prometheus metrics](https://fetchshelf.readthedocs.io/en/v2026.11.15/configuration/#prometheus-metrics).
- `covers_from_mam`: how many covers each Link run fetches from MAM for book folders without an image, `0` (none) by
  default (#101). See [Covers](https://fetchshelf.readthedocs.io/en/v2026.11.15/user-guide/#covers).
- A torrent's page links to the torrent on MAM (#105).

### Changed

- `upload_amounts` in `[bonus]` may hold only 50 and 100 GB, the amounts MAM allows automated purchases of, and
  defaults to `[100, 50]` (#104, #111).
- The Upgrade job leaves books of a collection alone, and they aren't marked *could be upgraded* (#105).
- The Clean job never takes two books of one collection for copies of each other (#109).
- Account page: the *Important Tracker Errors* warning says MAM keeps counting a tracker error for 48 hours after it
  last occurred, and the link to the snatch lists on MAM opens in a new tab.

### Fixed

- Collections are told apart better from their file names (#106–#110): trailing release tags and years in brackets,
  underscored and hyphenated names, series names ending in a number, one series spelled several ways, folders of
  issues, a volume given by a folder's name, variant covers, magazine issues by month and year, and a book the torrent
  holds twice. The torrent's title names a series only when it adds no collection words, authors or years.

## 2026.11.14 - 2026-11-18

A book in two languages is two books: the Follow job decides each language on its own, and two editions with the same
title get folders of their own. Downloads can go to a chosen qBittorrent, into a chosen folder. On the Search page,
authors, narrators and series link to more of the same.

### Upgrading

- The database is upgraded when the new version starts, and going back to an older version is not possible. With
  a [`[backup]`](https://fetchshelf.readthedocs.io/en/v2026.11.14/configuration/#backup) section, a backup is taken first, named
  `data-<time>-2026.11.13-before-2026.11.14.db`; without one, back up your data folder yourself.
- **The Follow job finds more.** Each language is now decided on its own, so the first search of each follow can list
  many editions in other languages that it skipped before. On *Auto*, those the `[[follow.grab]]` blocks take are
  downloaded in that run, within the unsatisfied-snatch budget. To see them first, set `dry_run = true` in
  [`[follow]`](https://fetchshelf.readthedocs.io/en/v2026.11.14/configuration/#follow) for the first runs. A block that took a free edition in one language, else
  one in another, now takes both, each judged on its own.
- **Two editions in one folder.** Where an earlier version linked two editions of a title, in different languages,
  into one folder, both are flagged *Different folder* on the **Torrents** page. Relinking one of them gives it a folder
  of its own, ending with its language, such as `Gamma (Greek)`. See
  [Languages in folder names](https://fetchshelf.readthedocs.io/en/v2026.11.14/configuration/#languages-in-folder-names).
- Nothing else is needed: without `client` and `save_path`, downloads go to the first `[[qbittorrent]]` as before.

### Added

- `client` and `save_path` on `[[autograb]]`, the lists, `[follow]`, `[upgrade]` and `[[tag]]` rules: which
  qBittorrent receives a download, by its new `name`, and the folder it is saved in. They are decided like the
  category: the block's own, else the first matching tag rule's. A library picks the downloads up by that folder
  (`download_dir`) or their category (#98). See [`[[qbittorrent]]`](https://fetchshelf.readthedocs.io/en/v2026.11.14/configuration/#qbittorrent).
- Search page: with two or more qBittorrents, **in qBittorrent** next to **Download into** chooses which one a
  download goes to, remembered like the category (#98).
- Search page: authors, narrators and series link to a search for more of the same, the name as a phrase in that field
  only, keeping the chosen language (#97). See
  [Finding and downloading books](https://fetchshelf.readthedocs.io/en/v2026.11.14/user-guide/#finding-and-downloading-books).
- `language_in_folder` on a `[[library]]`: `"on_clash"`, the default, ends a book's folder with its language only when
  the plain folder holds the same title in another language; `"other"` does so for every book not in the first of
  the top-level `languages` (#96).
- The **Following** lists show a Language column, and filter by it, when the finds span more than one language (#96).

### Changed

- The Follow job groups, owns and decides its finds per language: an English copy no longer stands in for the
  Hungarian edition, or the other way round. Audiobookshelf's books count as owned in their own language; one without
  a language counts in every language (#96). See
  [Completing series and following authors](https://fetchshelf.readthedocs.io/en/v2026.11.14/user-guide/#completing-series-and-following-authors).

## 2026.11.13 - 2026-11-18

Wanted books can come from a CSV or text file, such as a Goodreads or StoryGraph export, and MAM bookmarks show on
the Lists page with what happened to each. Comic and manga libraries can be laid out one folder per series, the way
Komga reads them. The Follow job can follow every series in Audiobookshelf. A new `[seeding]` section estimates which
torrents still need seeding. The Jobs page can dry-run a job, and each run's history lists what it selected.

### Upgrading

- The database is upgraded when the new version starts, and going back to an older version is not possible. With
  a [`[backup]`](https://fetchshelf.readthedocs.io/en/v2026.11.13/configuration/#backup) section, a backup is taken first, named
  `data-<time>-2026.11.12-before-2026.11.13.db`; without one, back up your data folder yourself.
- The log lines of `[[hardcover_list]]` jobs start with `List` instead of `Hardcover list`, and the Account page names
  a list that is talking to MAM the same way. Change any log filter that looks for the old words.
- An `[[autograb]]` with `type = "bookmarks"` and the new `[[bookmarks_list]]` can't both be configured: startup
  stops, saying so. Choose one; see [`[[bookmarks_list]]`](https://fetchshelf.readthedocs.io/en/v2026.11.13/configuration/#bookmarks_list).
- **Dry run** buttons appear on the Jobs page. `dry_job_buttons = false` hides them.
- Nothing else is needed: every other new feature is off until it is configured.

### Added

- `[[file_list]]`: wanted books from a CSV or text file, such as a Goodreads or StoryGraph export, a spreadsheet saved
  as CSV, or titles typed one per line. MAM is searched for them as for a Hardcover list, and they show on the
  **Lists** page. The file is read on every run; `--check-config` says how many books it holds (#90). See
  [Books you want, from a file](https://fetchshelf.readthedocs.io/en/v2026.11.13/user-guide/#books-you-want-from-a-file).
- `[[bookmarks_list]]`: the torrents bookmarked on MAM on the **Lists** page, each with what happened to it, selected
  through its `grab` blocks. A bookmark not yet taken is looked at again on every run, so one that turns freeleech is
  taken (#91). See [Your MAM bookmarks as a list](https://fetchshelf.readthedocs.io/en/v2026.11.13/user-guide/#your-mam-bookmarks-as-a-list).
- `layout = "series"` on a `[[library]]`: comics and manga linked one folder per series, each book a file named
  `Series #001 - Title`, with a Mylar `series.json`, the shape Komga reads. Books without a series go into
  `oneshots_dir` (#92). See [Comics and manga for Komga](https://fetchshelf.readthedocs.io/en/v2026.11.13/user-guide/#comics-and-manga-for-komga).
- `[follow] audiobookshelf_series`: every series in Audiobookshelf followed, so series from before Fetchshelf are
  completed too. The books Audiobookshelf has count as owned, and its series show on the **Series** page. A series
  you stop following isn't followed again (#93). See
  [Series from Audiobookshelf](https://fetchshelf.readthedocs.io/en/v2026.11.13/user-guide/#series-from-audiobookshelf).
- `[seeding]`: the **Seeding** job estimates from qBittorrent's seeding time which torrents still need seeding to
  satisfy MAM, 72 hours within 30 days by default. It can tag them (`tag_unsatisfied`), holds back `on_cleaned` for a
  retired copy until it is satisfied, and sends `unsatisfied_not_seeding` when one stops seeding too early. The
  **Account** page shows the estimate next to MAM's counts, the **Torrents** page has a *Seeding* filter, and
  **Unlink** and **Remove from qBittorrent with data** warn about a torrent that isn't satisfied (#88). See
  [Seeding torrents until MAM counts them as satisfied](https://fetchshelf.readthedocs.io/en/v2026.11.13/user-guide/#seeding-torrents-until-mam-counts-them-as-satisfied).
- **Jobs** page: **Dry run** next to **Run now** for every autograb, list, Follow and Upgrade, which runs the job once
  selecting nothing. A job's history lists under each run what it selected, or would have, and turned down (#89).
  See [Background jobs](https://fetchshelf.readthedocs.io/en/v2026.11.13/user-guide/#background-jobs).
- **Account** page, **Over time**: a chart of the unsatisfied snatches (#83).

### Changed

- **Config** page: each autograb, list, `[follow]` and `[upgrade]` shows its own `unsat_buffer`, `wedge_buffer` and
  `max_active_downloads`, or the top-level value that applies (#82).
- **Lists** page: a *Source* column, as lists can come from Hardcover, a file or MAM's bookmarks (#90, #91).

## 2026.11.12 - 2026-11-17

The Account page keeps a daily snapshot of the account and shows the ratio, upload, bonus points and wedges over
time. Bulk actions on the Torrents page run in the background with their progress shown, and keep going when the
page is left. Backups are named after the version to run them on. `allowed_hosts` and an optional OPDS login close
two openings. `[[snatchlist]]` is removed, to conform to MAM's rules for automation.

### Upgrading

- **`[[snatchlist]]` is removed, to conform to MAM's rules for automation.** MAM's API documentation lists the only
  parts of the site that may be automated, and the snatch lists `[[snatchlist]]` read aren't among them. A
  configuration that still has a `[[snatchlist]]` section stops startup, saying so: remove the sections. The
  torrents they recorded are kept. To keep your own uploads recorded as owned, an `[[autograb]]` with
  `type = "mine"` and `cost = "metadata_only_add"` does it through the search. See
  [`[[snatchlist]]` (removed)](https://fetchshelf.readthedocs.io/en/v2026.11.12/configuration/#snatchlist-removed).
- The database is upgraded when the new version starts, and going back to an older version is not possible. With
  a [`[backup]`](https://fetchshelf.readthedocs.io/en/v2026.11.12/configuration/#backup) section, a backup is taken first; without one, back up your data folder
  yourself. That first backup is still named the old way, without a version: the database records its version
  from this release on.

### Added

- **Account** page: **Over time**, one snapshot of the account a day, from the reads the jobs make anyway. Charts
  of the last 90 days show the ratio, upload, bonus points and wedges, and a table gives each month's change. Every
  day can be downloaded as CSV (#78). See [Account](https://fetchshelf.readthedocs.io/en/v2026.11.12/user-guide/#account).
- `allowed_hosts`: the host names Fetchshelf answers to. Any other name gets HTTP 400, which stops other web pages
  using Fetchshelf through DNS rebinding. Left empty, any name is answered, as before (#80). See
  [Security and privacy](https://fetchshelf.readthedocs.io/en/v2026.11.12/user-guide/#security-and-privacy).
- `[opds]` `username`, plus `password` or `password_file`: an optional login for the OPDS catalog, which readers
  send as HTTP Basic. The rest of Fetchshelf stays without a login (#80). See
  [Reading on an e-reader](https://fetchshelf.readthedocs.io/en/v2026.11.12/user-guide/#reading-on-an-e-reader-opds).
- An older version starting on a database a newer one has used says so in the log, and which backups it can use
  (#76).

### Changed

- **Torrents** page: Clean, Refresh, Relink, Unlink and Set category run in the background, one torrent at a time.
  The page comes back at once, and a panel above the list shows the progress, with **Cancel**, then how it went,
  with each torrent's problem on its own line. The **Jobs** page lists them too. They take turns with the Link and
  Clean jobs, one torrent at a time, so they never work on the same files at once (#79). See
  [Torrents](https://fetchshelf.readthedocs.io/en/v2026.11.12/user-guide/#torrents).
- Backups are named after the version to run them on: `data-<time>-2026.11.12-scheduled.db`, and
  `data-<time>-2026.11.12-before-<new version>.db` before an upgrade (#76). See
  [Backups while Fetchshelf runs](https://fetchshelf.readthedocs.io/en/v2026.11.12/user-guide/#backups-while-fetchshelf-runs).
- On a wide screen, the blank margins left and right of the pages are half as wide, so tables such as the Torrents
  list have room (#75).

### Fixed

- Leaving the **Torrents** page during a bulk relink, unlink or category change stopped it halfway (#79).

### Removed

- `[[snatchlist]]`, to conform to MAM's rules for automation: MAM's API documentation lists the only parts of the
  site that may be automated, and the snatch lists aren't among them (#77).

## 2026.11.11 - 2026-11-17

Hardcover lists find more books, by ISBN and ASIN, and stop searching every day for the ones MAM doesn't have. Every
Hardcover list, `[follow]` and `[upgrade]` can have its own buffers and cap. Libraries can keep their folder names
safe for Windows and shared disks, and choose their own folder layout. A name too long for the file system is
shortened instead of failing to link.

### Upgrading

- The database is upgraded when the new version starts, and going back to an older version is not possible. With
  a [`[backup]`](https://fetchshelf.readthedocs.io/en/v2026.11.11/configuration/#backup) section, a backup is taken first; without one, back up your data folder
  yourself.
- Torrents a `[[hardcover_list]]` without a `name` selected before the upgrade, and that are still waiting to be
  downloaded, don't count towards that list's new `max_active_downloads` or buffers; what it selects from now on
  does.
- Folder names don't change unless a library sets `folder_names` or `folder_template`. If one does, the books it
  affects are flagged *Different folder*; tick them on the **Torrents** page and use **Relink selected** to move them.

### Added

- `[[hardcover_list]]`: a MAM torrent with one of the book's ISBNs or ASINs, from its most read editions on
  Hardcover, is the book, whatever its title. When the first page of results holds nothing like the book, the second
  page is read too (#70).
- `[[hardcover_list]]`: a book MAM has had nothing for for a week is searched weekly instead of daily. The **Lists**
  page says when each book is next searched, and **Search again** has the next run search for it (#70).
- `unsat_buffer`, `wedge_buffer` and `max_active_downloads` in every `[[hardcover_list]]`, in `[follow]` and in
  `[upgrade]`, as on a named autograb. The **Account** page gives each block with its own a line (#71). See
  [Protecting your account](https://fetchshelf.readthedocs.io/en/v2026.11.11/user-guide/#protecting-your-account).
- **Config** page: a tag rule shows its whole filter, its rules are numbered, and **Preview** shows which torrents'
  category or tags applying it would change, in a dialog, before **Apply** changes exactly those (#72).
- The Link job's result says how many torrents wait for a MAM lookup in the next runs, and how many MAM didn't know,
  instead of "Nothing new to link" (#72). See
  [Torrents wait to be linked](https://fetchshelf.readthedocs.io/en/v2026.11.11/user-guide/#torrents-wait-to-be-linked).
- `folder_names = "portable"` per `[[library]]` makes folder names safe on Windows, SMB shares, exFAT and NTFS:
  `Dune: Part One` becomes `Dune - Part One` (#73). See [Folder naming](https://fetchshelf.readthedocs.io/en/v2026.11.11/configuration/#folder-naming).
- `folder_template` per `[[library]]` chooses the folder layout, such as `{series}/[{series_number} - ]{title}` for
  comics (#73). See [Folder template](https://fetchshelf.readthedocs.io/en/v2026.11.11/configuration/#folder-template) and
  [Folder names](https://fetchshelf.readthedocs.io/en/v2026.11.11/user-guide/#folder-names).
- **Config** page: each library's folder names and template (#73).

### Changed

- A `cost = "wedge"` download never downloads on ratio, so a spent ratio budget no longer holds it back, and a
  `try_wedge` download that got a wedge no longer counts against the ratio budget while it downloads (#71).
- `hardlink_or_copy` and `hardlink_or_symlink` fall back only when a hardlink can't be made there, such as on
  another disk. A full disk, a permission problem or a read-only library is reported on the **Errors** page with its
  name, such as `ENOSPC`, instead of turning into a copy that fails too (#72).

### Fixed

- A Hardcover list book whose selection was removed before it was downloaded, or whose copy is gone, was never
  searched for again (#70).
- A Hardcover list book found as both an audiobook and an ebook could make two selections when
  `max_active_downloads` had room for one (#71).
- A folder name longer than the file system allows, such as a long title with a subtitle, failed to link. It is now
  shortened to 255 bytes, keeping the series number, edition and narrator (#73).
- With `exclude_narrator_in_library_dir = true`, every book was flagged *Different folder* on the Link run after it
  was linked (#73).
- A wrong value in the configuration, such as `media_type = "ComicBook"` where a list goes or an unknown `method`,
  stopped startup with only "Exception during deserialization". The message now names the setting or the values it
  can be (#73).

## 2026.11.10 - 2026-11-16

Fetchshelf can back up its own database while it runs, and does so before an upgrade changes it, and the
**Torrents** page exports its list as CSV or JSON. A download from the Search page can no longer queue a book twice
alongside an automatic search.

### Upgrading

- No database change: going back to 2026.11.9 is possible.
- A configured block named `Backup` (an `[[autograb]]`, `[[snatchlist]]` or `[[hardcover_list]]` with
  `name = "Backup"`) stops startup, as `Backup` is now the name of a built-in job. Rename it.
- To have backups taken, add a [`[backup]`](https://fetchshelf.readthedocs.io/en/v2026.11.10/configuration/#backup)
  section with a `folder`. Without one, nothing changes. From then on, an upgrade that changes the database is
  backed up first.

### Added

- `[backup]`: the optional Backup job copies the database safely while Fetchshelf runs, once a day by default,
  keeping the last 7, and a backup is taken before every upgrade that changes the database, keeping the last 3. A
  backup that fails before an upgrade stops startup rather than upgrading without it. See
  [`[backup]`](https://fetchshelf.readthedocs.io/en/v2026.11.10/configuration/#backup) and
  [Backups while Fetchshelf runs](https://fetchshelf.readthedocs.io/en/v2026.11.10/user-guide/#backups-while-fetchshelf-runs),
  which also says how to restore one (#65).
- **Torrents** page: **Export** as **CSV** or **JSON** downloads the list as it is searched, filtered and sorted,
  every page of it (#65).

### Changed

- While a job is talking to MAM, a search, a **Download** or an **Import** on the Search page, and a search
  result's details, wait up to 10 seconds for it, then say which job is busy and offer **Try again**, instead of
  talking to MAM alongside it (#67).

### Fixed

- A download from the Search page could run its selection at the same time as an autograb's, so the same book
  could be queued twice (#67).
- The Upgrade job and Hardcover lists sent MAM characters its search reads as operators, such as `~` or a leading
  `-`: a title like "Encyclopaedia Eorzea ~The World of Final Fantasy XIV~ Volume I" made the search fail with
  "Your search is only not". A book whose search failed this way is searched again after `search_again_after`
  (#68).

## 2026.11.9 - 2026-11-15

Fetchshelf can spend MAM bonus points before they pass the cap: on a wedge a day, VIP and upload credit. Unlinking
the last book of a series or an author now removes the folders it leaves empty, and the documentation has its own
site, with a version for every release from this one.

### Upgrading

- A configured block named `Bonus` (an `[[autograb]]`, `[[snatchlist]]` or `[[hardcover_list]]` with
  `name = "Bonus"`) stops startup, as `Bonus` is now the name of a built-in job. Rename it.
- If you spend bonus points with a script or tool of your own, stop it before you add a `[bonus]` section, or both
  will buy. Without a `[bonus]` section, Fetchshelf spends nothing.
- The database is upgraded when the new version starts, and going back to an older version is not possible.
  Back up your data folder first.

### Added

- `[bonus]`: the optional Bonus job spends bonus points, every two hours by default: a wedge every
  `wedge_every` hours, a VIP top-up with `vip = true`, then upload credit in `upload_amounts` steps, keeping
  `keep_points` back. Every purchase is on the **Events** page, and the **Account** page shows what the next run
  would buy. See [`[bonus]`](https://fetchshelf.readthedocs.io/en/v2026.11.9/configuration/#bonus) and
  [Spending bonus points](https://fetchshelf.readthedocs.io/en/v2026.11.9/user-guide/#spending-bonus-points) (#62).
- The `bonus_spent` notification event, sent for each purchase (#62).
- The documentation is published at [fetchshelf.readthedocs.io](https://fetchshelf.readthedocs.io/en/stable/), with a version for every release from
  this one; choose yours in the version menu (#59).

### Changed

- **Events** page: details longer than 200 characters, such as a long library path, show their first 180
  characters and **... more**, which opens the whole text (#63).

### Fixed

- Unlinking an author's only book left the empty series and author folders in the library. Unlink, Clean and
  relinking now remove the folders they leave empty, up to but never including the `library_dir` (#61, #29).

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
