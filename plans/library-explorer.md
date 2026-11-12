# Library explorer: browse the library in the web UI — implementation plan

Not yet an issue: written up to decide whether it's worth building at all (see "Before deciding").

## Context

The OPDS catalog (#20) lets an e-reader browse, search and download the ebook libraries, by recently added,
author and series. The web UI has nothing like it. The **Torrents** page lists every book, but as a
*management* view: statuses, problems, filters, bulk actions. It isn't built for finding a book to read or
listen to, and nothing in the UI covers audiobooks the way OPDS covers ebooks.

The idea: a **Library** page that browses everything Fetchshelf has linked, audiobooks included, the way the
OPDS catalog does. It helps you find the book, then hands it to the app that plays or shows it best.

### Not a player

Fetchshelf won't play or display books itself. A real audiobook player needs resume position, speed,
chapters, multi-file books played as one, and background playback on phones. An ebook reader needs a reader
library and progress tracking. Both need per-user progress, and Fetchshelf has no users or login.
Audiobookshelf does all of that, with progress sync and native apps, and Fetchshelf already integrates with
it; OPDS readers cover ebooks. The explorer links to those apps and offers downloads.

## Before deciding

**Try Audiobookshelf as the ebook platform first.** It may already be the explorer and player wanted:

- it shows ebooks and audiobooks in one library, with series, authors, collections and search;
- its web and mobile apps read EPUB and PDF and sync progress, and its OPDS-less setup matters less if
  reading happens in its own apps;
- Fetchshelf already matches books to Audiobookshelf items and pushes metadata, and its `metadata.json` files
  give Audiobookshelf what it needs for ebook-only items.

If Audiobookshelf covers it, this page adds little beyond an "Open in Audiobookshelf" button, which the torrent
page already has. Things to check while trying it:

- whether an ebook library and an audiobook library side by side, or one mixed library, works better with
  Fetchshelf's folders;
- reading on the devices actually used: phone, tablet, e-reader. Audiobookshelf has no OPDS, so an e-ink
  reader still needs Fetchshelf's catalog;
- whether the metadata it shows for ebook-only items is good, now that `metadata.opf` can be written too.

## Open questions

1. **Multi-file audiobooks:** download as one zip built on the fly, as separate files, or both?
   *Proposed:* both. The zip is convenient, but it can be several GB and can't resume an interrupted download,
   so the separate files are listed too.
2. **A preview button** using the browser's own `<audio>` element: in or out? It costs almost nothing.
   Browsers play m4b and mp3, and the file endpoint already supports range requests, so seeking works. But it
   has no saved position and no chapters. *Proposed:* in, labelled as a preview, for single-file audiobooks only.
3. **Audiobook covers:** EPUB and CBZ covers already come from the OPDS cover cache. Audiobooks need another
   source. Options:
   - a. the cover embedded in an m4b (the MP4 `covr` atom), or in an mp3's ID3 `APIC` frame;
   - b. MAM's cover, which needs a MAM lookup per book, since the stored metadata lacks `poster_type`;
   - c. Audiobookshelf's cover for matched books, which needs its API token in the browser and is awkward.

   *Proposed:* a, with no cover when the file has none.
4. **Navigation:** a new **Library** entry, or fold it into **Torrents**? *Proposed:* a new entry next to
   **Torrents**, with the user guide explaining the split: Library for finding and opening books, Torrents
   for managing them.

## Approach

### 1. A catalog of every linked book

Generalise the OPDS catalog rather than write a second one:

- `OpdsBook` and `OpdsCatalog` become a format-neutral `LibraryBook` and `LibraryCatalog` in
  `Fetchshelf.Core/Library/`, with the media type and the audio and ebook files of each book.
- The OPDS reader keeps serving only the ebook media types: a filter over the shared catalog, with no change to
  the feeds.
- The one-minute snapshot cache becomes the shared cache for both, so the OPDS feeds and the Library page read
  the database once between them.
- Books are matched to libraries by folder (`LibraryFolders`), as now. Audio files are the `LinkedFilesJson`
  entries with the chosen audio extension, ebook files those with the chosen ebook extension; a book can have
  both.

### 2. The Library page: `src/Fetchshelf.Web/Pages/Library/`

Server-rendered Razor Pages with htmx, like the rest of the UI:

- **Views:** Recently added, Authors, Series (in series order, with each book's number) and Libraries, plus a
  search box using the shared search matcher. A filter for **Audiobooks / Ebooks / All**.
- **Layout:** a grid of covers with title, author and series line, paged like the other lists (the shared
  page-size choice). A book opens a detail panel with the full metadata and the actions below.
- **Actions per book:**
  - **Open in Audiobookshelf** when the book is matched, using the same link as the torrent page;
  - **Download** for each file: the ebook, a single-file audiobook, or each part;
  - **Download all as zip** for a multi-file audiobook (open question 1);
  - **Preview** for a single-file audiobook (open question 2);
  - **Details**, linking to the book's torrent page for management.

### 3. File and zip downloads

- Reuse the OPDS download endpoint's rules: the file comes from the catalog, never from the request, and must
  resolve inside the book's folder; served with range support and a `Title - Author.ext` name. Move that
  code to a shared helper and give it the audio MIME types (`m4b` and `m4a` as `audio/mp4`, `mp3` as
  `audio/mpeg`, `ogg` as `audio/ogg`, `flac` as `audio/flac`).
- **Zip:** stream a `ZipArchive` straight to the response, with no compression (audio doesn't compress) and
  without building it on disk. Name the entries after the files Fetchshelf placed, keeping `Disc N` folders.
  No `Content-Length` and no resuming, which is why the separate files stay available.

### 4. Audiobook covers

If open question 3 lands on embedded covers: a small reader for the m4b `covr` atom and the mp3 ID3 `APIC`
frame, added to `BookCoverExtractor`, with the same 5 MiB cap and the same cover cache.

### 5. Documentation

A user guide section on the Library page, and on how it relates to the Torrents page, Audiobookshelf and the
OPDS catalog. The pages table in the user guide and `CLAUDE.md`'s list of page folders get the new page.

## Order of work

1. Generalise the catalog (Step 1), with the OPDS tests passing unchanged. Worth doing on its own.
2. The Library page with views, search and the ebook and single-file downloads.
3. Multi-file zips and the preview, as the open questions decide.
4. Audiobook covers.
5. Docs, then trying it on a phone and a desktop browser.

One feature branch and PR, or two if steps 1 and 2 land first.

## Risks

- **Overlap.** Three ways to see the same books (Torrents, Library, Audiobookshelf), plus OPDS. If the
  Library page doesn't do something the others don't, it is clutter. This is the main reason to try
  Audiobookshelf first.
- **Exposure.** Like OPDS, the page lets anyone who reaches Fetchshelf download every book, audiobooks
  included. Same answer as before: a trusted network, or a reverse proxy with a login.
- **Big downloads.** Multi-GB zips over a slow or flaky connection fail without resuming; the separate files are
  the fallback.
- **Preview expectations.** A preview that people use as a player will disappoint (no position, no chapters).
  Keeping it single-file and calling it a preview limits that.

## Verification

- Core tests for the generalised catalog: audio-only, ebook-only and both-format books; series and author views
  across formats; the OPDS filter still serving only ebook media types.
- Data tests for the catalog reader with audiobooks, and for the m4b and mp3 cover readers if they're built.
- Web tests: each view and the format filter, downloads with range requests, a zip containing exactly the placed
  files in their `Disc N` folders, the refusal of a path outside the book folder, and the Audiobookshelf link
  only for matched books.
- The OPDS scale test, extended to the Library page's views at 10,000 torrents.
- By hand: browse, download and preview on a phone and on a desktop browser.
