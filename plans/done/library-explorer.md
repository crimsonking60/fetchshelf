# Library explorer: browse the library in the web UI — implementation plan

Decided 2026-11-21: Audiobookshelf was tried as the ebook platform and set aside, so the page is built. Every open
question takes its proposed answer.

Done 2026-11-21: steps 1-4 merged as #162 (shared catalog), #163 (Books page), #164 (zip and preview) and #165
(folder and audio covers, and CBR covers). The docs of step 5 went in with each step. Not done here: trying it by
hand on a phone and a desktop browser (playing the preview, the book dialog, the `[opds]` login prompt), and pruning
the `covers` folder of books no longer in the library.

Reviewed 2026-11-21 against the app as it is now; what changed since the first version is listed at the end.

## Context

The OPDS catalog (#20) lets an e-reader browse, search and download the ebook libraries, by recently added,
author, series and library. The web UI has nothing like it. The **Torrents** page lists every book, but as a
*management* view: statuses, problems, filters, bulk actions, export. The **Series** page lists the series in
the library with their missing books, for completing them and following them. Neither is built for finding a
book to read or listen to, and nothing in the UI covers audiobooks the way OPDS covers ebooks.

The idea: a page that browses everything Fetchshelf has linked, audiobooks included, the way the OPDS catalog
does. It helps find the book, then hands it to the app that plays or shows it best.

### Not a player

Fetchshelf won't play or display books itself. A real audiobook player needs resume position, speed,
chapters, multi-file books played as one, and background playback on phones. An ebook reader needs a reader
library and progress tracking. Both need per-user progress, and Fetchshelf has no users or login.
Audiobookshelf does all of that, with progress sync and native apps, and Fetchshelf already integrates with
it; OPDS readers cover ebooks. The explorer links to those apps and offers downloads.

## Before deciding

**Try Audiobookshelf as the ebook platform first.** It may already be the explorer and player wanted:

- it shows ebooks and audiobooks in one library, with series, authors, collections and search;
- its web and mobile apps read EPUB and PDF and sync progress, so its lack of OPDS matters less if reading
  happens in its own apps;
- Fetchshelf already matches books to Audiobookshelf items and pushes metadata, and its `metadata.json` and
  `metadata.opf` files give Audiobookshelf what it needs for ebook-only items.

If Audiobookshelf covers it, this page adds little beyond an "Open in Audiobookshelf" button, which the torrent
page already has. Things to check while trying it:

- whether an ebook library and an audiobook library side by side, or one mixed library, works better with
  Fetchshelf's folders, including series-layout libraries and split collections;
- reading on the devices actually used: phone, tablet, e-reader. Audiobookshelf has no OPDS, so an e-ink
  reader still needs Fetchshelf's catalog;
- whether the metadata it shows for ebook-only items is good with `metadata.opf` written, and whether the
  covers from `covers_from_mam` are enough.

## Open questions

1. **Multi-file audiobooks:** download as one zip built on the fly, as separate files, or both?
   *Proposed:* both. The zip is convenient, but it can be several GB and can't resume an interrupted download,
   so the separate files are listed too.
2. **A preview button** using the browser's own `<audio>` element: in or out? It costs almost nothing.
   Browsers play m4b and mp3, and the file endpoint already supports range requests, so seeking works. But it
   has no saved position and no chapters. *Proposed:* in, labelled as a preview, for single-file audiobooks only.
3. **Covers.** EPUB and CBZ covers already come from the OPDS cover cache (`OpdsCoverCache`, the `covers/`
   folder beside the database). Audiobooks, PDFs and other formats need another source. Options:
   - a. **The image in the book's folder.** With `covers_from_mam` above 0 the Link job's `CoverWriter` writes
     MAM's cover there as `cover.jpg`; a split collection's book gets the cover found in the torrent; a user
     may put one there. Audiobookshelf reads the same file. No MAM request is needed. Series-layout libraries
     have no per-book folder and no such image.
   - b. The cover embedded in an m4b (the MP4 `covr` atom) or in an mp3's ID3 `APIC` frame.
   - c. Audiobookshelf's cover for matched books, which needs its API token in the browser and is awkward.

   Fetching MAM's cover from the page itself is out: MAM's rules allow its cover images only on the Search
   page and through the Link job's `covers_from_mam`, and the page must not call MAM.

   *Proposed:* a, then the file's embedded cover (EPUB and CBZ as now, b for m4b and mp3), else none. The
   folder image helps the OPDS catalog too: PDFs and books whose EPUB declares no cover get one. A folder image
   is not permanent with `covers_from_mam = 0`: **Refresh and relink** removes one Fetchshelf didn't write
   (decision D5 of the documentation review kept that), so the cover cache must notice it gone.
4. **Name and place in the menu.** The bar now has **Search**, **Torrents**, **Selected**, **Jobs** and
   **Errors**, then the **Library** menu (Series, Following, Lists, Duplicates, Library check) and the
   **Insight** menu (Events, Statistics). A page called *Library* would clash with the menu.
   *Proposed:* **Books**, as the first entry of the **Library** menu (`/Books`, `Pages/Books/`), with the user
   guide explaining the split: Books for finding and opening books, Torrents for managing them, Series for
   completing series.
5. **Downloads without a login.** The OPDS catalog can now require a login (`[opds]` `username` and
   `password`); the web UI has none, by design. A Library page with downloads lets anyone who reaches the web
   UI download every book, audiobooks included, even where OPDS is behind its login. Options: downloads always
   on (the guide says so); downloads only when an `[opds]` section exists, behind the same login; or a setting.
   *Proposed:* the second: the page browses without a login, and its download and cover endpoints live under
   the OPDS login when one is set. Needs a decision before step 2.

## Approach

### 1. A catalog of every linked book

Generalise the OPDS catalog rather than write a second one:

- `OpdsBook` and `OpdsCatalog` (`Fetchshelf.Core/Opds/`) become a format-neutral `CatalogBook` and
  `BookCatalog` in `Fetchshelf.Core/Catalog/`, with the media type and the audio and ebook files of each book.
  Not `Fetchshelf.Core.Library`: a namespace named `Library` would clash with the configuration's `Library`
  class used throughout.
- `OpdsCatalogReader` (`Fetchshelf.Data/Opds/`) reads only rows with an ebook extension and only the libraries
  `[opds] libraries` names. The shared reader reads every linked book of every library (rows with
  `ChosenAudioExtension` or `ChosenEbookExtension`); the OPDS feeds filter that down to the ebook media types
  and their served libraries, with no change to the feeds.
- `OpdsCatalogCache`'s one-minute snapshot becomes the shared cache for both, so the OPDS feeds and the page
  read the database once between them. It must work without an `[opds]` section.
- Books are matched to libraries by folder (`LibraryFolders.IndexContaining`), as now. Audio files are the
  `LinkedFilesJson` entries with the chosen audio extension, ebook files those with the chosen ebook extension;
  a book can have both. Split collections need nothing special: each book is its own record (`<hash>-<n>`)
  with its own folder, and the collection's record has no folder. In a series-layout library many books share
  one folder, and each book's files are its own `LinkedFilesJson` entries.

### 2. The page: `src/Fetchshelf.Web/Pages/Books/`

Server-rendered Razor Pages with htmx, like the rest of the UI:

- **Views:** Recently added, Authors, Series (in series order, with each book's number) and Libraries, plus a
  search box using `TorrentSearchMatcher`. A filter for **Audiobooks / Ebooks / All**.
- **Layout:** a grid of covers with title, author and series line, paged like the other lists (`_Pager` and
  the shared page-size choice, `_PageSize`). A book opens a detail panel with the full metadata and the actions
  below.
- **Actions per book:**
  - **Open in Audiobookshelf** when the book is matched, the same link as the torrent page's (`AbsLink`);
  - **Download** for each file: the ebook, a single-file audiobook, or each part;
  - **Download all as zip** for a multi-file audiobook (open question 1);
  - **Preview** for a single-file audiobook (open question 2);
  - **Details**, linking to the book's torrent page for management.
- The Series view links each series to its row on the **Series** page, for its missing books.
- The menu entry goes in `NavModel` (`Fetchshelf.Web/Navigation/`), with `NavModelTests`.

### 3. File and zip downloads

- Reuse the OPDS download endpoint's rules (`OpdsEndpoints`, `/opds/books/{id}/files/{index}`): the file comes
  from the catalog, never from the request, and must resolve inside the book's folder; served with range support
  and a `Title - Author.ext` name. Move that code to a shared helper and give it the audio MIME types (`m4b` and
  `m4a` as `audio/mp4`, `mp3` as `audio/mpeg`, `ogg` as `audio/ogg`, `flac` as `audio/flac`). Where it is
  mounted and behind which login follows open question 5.
- **Zip:** stream a `ZipArchive` straight to the response, with no compression (audio doesn't compress) and
  without building it on disk. Name the entries after the files Fetchshelf placed, keeping `Disc N` folders.
  No `Content-Length` and no resuming, which is why the separate files stay available.

### 4. Covers

If open question 3 lands as proposed: `OpdsCoverCache` looks for an image in the book's folder first, keyed by
that file's size and time as it keys book files now, then reads the book file. A small reader for the m4b
`covr` atom and the mp3 ID3 `APIC` frame is added to `BookCoverExtractor`, with the same 5 MiB cap.

### 5. Documentation

- A user guide section on the page, and on how it relates to the Torrents and Series pages, Audiobookshelf and
  the OPDS catalog, including what open question 5 decides about downloads.
- The pages table in the user guide, and `CLAUDE.md`'s list of page folders.
- If folder covers reach the OPDS catalog (step 4), one line in the OPDS section of the guide.

## Order of work

1. Generalise the catalog (Step 1), with the OPDS tests passing unchanged. Worth doing on its own.
2. The page with views, search and the ebook and single-file downloads, after open question 5 is decided.
3. Multi-file zips and the preview, as the open questions decide.
4. Covers: folder images (which also improves OPDS), then the m4b and mp3 readers.
5. Docs, then trying it on a phone and a desktop browser.

One feature branch and PR per step, or steps 1 and 2 together.

## Risks

- **Overlap.** Four ways to see the same books (Torrents, Series, the new page, Audiobookshelf), plus OPDS. If
  the page doesn't do something the others don't, it is clutter. This is the main reason to try Audiobookshelf
  first.
- **Exposure.** The page lets anyone who reaches Fetchshelf find every book, and, depending on open question 5,
  download them, audiobooks included. A trusted network or a reverse proxy with a login, as for the rest of
  the UI.
- **Big downloads.** Multi-GB zips over a slow or flaky connection fail without resuming; the separate files are
  the fallback.
- **Preview expectations.** A preview that people use as a player will disappoint (no position, no chapters).
  Keeping it single-file and calling it a preview limits that.

## Verification

- Core tests for the generalised catalog: audio-only, ebook-only and both-format books; series and author views
  across formats; the OPDS filter still serving only ebook media types and its `[opds] libraries`.
- Data tests for the catalog reader with audiobooks, split-collection books and a series-layout library; for
  folder covers and their removal; and for the m4b and mp3 cover readers if they're built.
- Web tests: each view and the format filter, downloads with range requests, a zip containing exactly the placed
  files in their `Disc N` folders, the refusal of a path outside the book folder, the Audiobookshelf link only
  for matched books, the login on downloads as open question 5 decides, and the menu entry.
- `PageScaleTests` (which already covers the OPDS feeds), extended to the page's views at 10,000 torrents.
- By hand: browse, download and preview on a phone and on a desktop browser.

## Changes in this review (2026-11-21)

- **Menu:** *Library* is now a menu, so the page needs another name (open question 4).
- **Covers:** `covers_from_mam` and split collections put a cover image in book folders, which gives
  audiobooks and PDFs a cover without a MAM request. Option b of the first version, a MAM lookup per book, is
  dropped: MAM's rules don't allow it from this page.
- **OPDS login:** `[opds]` can require a login, which raises the download question (open question 5).
- **Names and places:** the Series page, `TorrentSearchMatcher`, `NavModel`, `PageScaleTests` and the
  `Fetchshelf.Core.Library` clash; split collections and series-layout libraries in the catalog reader.
