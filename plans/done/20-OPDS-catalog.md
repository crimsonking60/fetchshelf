# Issue #20: Serve ebook libraries as an OPDS catalog — implementation plan

## Context

Fetchshelf organizes ebooks into libraries, with good metadata from MAM, but reading them on an e-reader needs a
separate library server, and none fits: Audiobookshelf has no OPDS feed; Grimmory reads neither of the
metadata files Fetchshelf writes; and other OPDS servers (Kavita, Komga, ...) mostly rely on the metadata
embedded in each EPUB. That throws away the series, numbering and clean author names Fetchshelf already
stores.

Issue #20 proposes serving the libraries as an OPDS catalog straight from Fetchshelf. Fetchshelf already
knows everything a catalog needs, for every linked book:

- MAM's metadata, in `TorrentEntity.MetadataJson`: title, authors, series and number, description,
  language, genres, ISBN.
- The book's folder (`LibraryPath`) and the exact files placed there (`LinkedFilesJson`, relative paths with
  sizes).
- Which ebook format was linked (`ChosenEbookExtension`).

So the work is feeds, downloads and covers. There is no scanner, no second database and no second container.

Decisions made:

- **OPDS 1.2** (Atom XML), which every OPDS reader supports: KOReader, Moon+ Reader, Thorium, Panels,
  KyBook and others. OPDS 2.0 (JSON) is out of scope.
- **Built into Fetchshelf**, as minimal-API endpoints under `/opds` in `Fetchshelf.Web`. It is not a
  standalone server.
- **No login**, like the rest of Fetchshelf. Use it on a trusted network, or behind a reverse proxy with authentication: OPDS
  readers send HTTP Basic credentials.
- **Ebooks only.** Audiobook files are never served.
- **No new dependencies.** The Atom XML is built with `System.Xml.Linq`, and EPUB and CBZ covers are read with
  `System.IO.Compression`.

## Status

Implemented on `feature/opds`, following this plan, with one change: author and series names are passed in
the query string rather than the path (see Step 2). Scale check at 10,000 seeded torrents: the first request
reads the catalog in about 0.5 s, and after that every feed answers in 1–5 ms. Step 7: tried on 2026-11-07
with Yomu on an iPad, which browsed the catalog and downloaded and opened an EPUB and a manga.

## Settled questions

Settled on 2026-11-07, each with the proposed answer:

1. **Which libraries by default?** Every library that has ebooks. `[opds] libraries` narrows it.
2. **An audiobook's companion PDF**, linked as its ebook side, is not served. Only books whose media type
   is an ebook type are served: Ebook, Manga, Comic Book and Periodical Ebook.
3. **Covers for books without one in the file** (PDFs, EPUBs without a cover): none in this issue. MAM's cover
   image, which needs a MAM lookup per book because the stored metadata lacks MAM's `poster_type`, can follow
   later.

## Configuration

```toml
[opds]
# Leave out the section to turn OPDS off. With it, the catalog is served at http://<fetchshelf>/opds.
libraries = ["Ebooks"]   # optional: the [[library]] names to serve; default every library with ebooks
```

- `FetchshelfSettings.Opds` (`OpdsSettings?`), with `Libraries` (`List<string>`, default empty = all).
- **Validation in `ConfigLoader`:** every name in `libraries` must be a `[[library]]` `name`. An unknown name
  is a `ConfigurationException` that names it, as for other references.
- **Environment:** `FETCHSHELF_CONF_opds_libraries` (comma-separated) creates the section if the file has
  none, like the Hardcover token does.
- **Config page:** an OPDS section showing whether it is on, the libraries it serves, and the catalog URL.

## Approach

### 1. The catalog: `src/Fetchshelf.Data/Opds/OpdsCatalogReader.cs`

This builds an in-memory catalog snapshot from the database:

- **Which torrents:** those with `LibraryPath` set (linked, not replaced or unlinked), a non-null
  `ChosenEbookExtension`, an ebook media type (settled question 2), and a `LibraryPath` under one of the served
  libraries' `library_dir`.
  - Torrents are matched to libraries by folder, as `LinkRunner.LibraryContaining` does, not by
    `LinkerName`. Unnamed libraries have no `LinkerName` to match on.
  - Metadata-only records and `no_link` libraries have no files, so they drop out.
- **Each book:** id (the info hash), title, subtitle, authors, series name and first number, description,
  language, genres, created date, library, and its ebook files. The files are the `LinkedFilesJson` entries
  whose extension is the chosen ebook format, usually one; several when a torrent has the book split into
  parts.
- **Caching:** the snapshot is cached in memory for one minute (`IMemoryCache` or a small singleton holding the
  snapshot and its time), so a reader paging through the catalog doesn't deserialize 10,000 metadata blobs on
  every request. The Link job doesn't need to invalidate it: a new book appearing within a minute is fine.
- **Sorting** happens in memory. `CreatedAt` is a `DateTimeOffset`, which SQLite can't `ORDER BY` (Step 7 of
  the scaling plan would change that). At 10,000 torrents this costs well under a second, once per minute.

The pure parts live in `src/Fetchshelf.Core/Opds/`, with no I/O: the `OpdsBook` record, grouping by author and
series, series ordering, and the search match (reusing `TorrentSearchMatcher`).

### 2. Feeds: `src/Fetchshelf.Core/Opds/OpdsFeedBuilder.cs`

Pure functions from catalog data to `XDocument`s, testable without a web host.

| Path | Kind | Contents |
|---|---|---|
| `/opds` | navigation | Root: *Recently added*, *Authors*, *Series*, *Libraries* (only when more than one), search |
| `/opds/recent?page=N` | acquisition | All served books, newest first |
| `/opds/authors?page=N` | navigation | Authors A–Z, each with a book count |
| `/opds/author?name=...&page=N` | acquisition | One author's books, by series and number, then title |
| `/opds/series?page=N` | navigation | Series A–Z, each with a book count |
| `/opds/series/books?name=...&page=N` | acquisition | One series' books, in series order |
| `/opds/libraries` | navigation | The served libraries |
| `/opds/libraries/{index}?page=N` | acquisition | One library's books, newest first |
| `/opds/search.xml` | OpenSearch description | Points at the search feed |
| `/opds/search?q=...&page=N` | acquisition | Every word in title, author or series, as on the Torrents page |

- **Content types:** `application/atom+xml;profile=opds-catalog;kind=navigation` or `...;kind=acquisition`,
  and `application/opensearchdescription+xml`.
- **Paging:** 50 entries per page, with `rel="first"`, `"previous"`, `"next"` and `"last"` links. Every feed
  has `rel="start"` (to `/opds`), `rel="self"` and `rel="search"`.
- **Links are relative** (`/opds/...`), so the feeds work under whatever host and port a reverse proxy uses.
  Fetchshelf has no path-prefix support, so serving under a sub-path such as `/fetchshelf/opds` is out of scope.
- **A book entry has:**
  - `id` `urn:fetchshelf:book:<info-hash>` and `updated` (the created date);
  - `title`, with the subtitle as in `metadata.json`; `author` (one per author); `dc:language` (the ISO 639
    code, reusing `LanguageCodes`); `category` (one per genre);
  - `summary` (the description, as text) and a series line ("Stormlight Archive #1") at the start of the
    content, since OPDS 1.2 has no standard series field;
  - per file, `link rel="http://opds-spec.org/acquisition"` with the format's MIME type and `length`;
  - `link rel="http://opds-spec.org/image"` and `".../image/thumbnail"` when a cover exists (Step 4).
- **Names go in the query string** (author, series), URL-encoded and matched exactly, not in the path: ASP.NET
  Core doesn't decode `%2F` in a path segment, so a name with a `/` would break there. A library is addressed by
  its index in the configuration, since names are optional.

### 3. Downloads: `/opds/books/{hash}/files/{n}`

- The file is the `n`th ebook file of that book in the catalog snapshot. Nothing from the request becomes part of
  a path: the full path is `LibraryPath` + the stored relative path. It is also checked to resolve inside
  `LibraryPath`, as defence in depth against a crafted `LinkedFilesJson`.
- Served with `Results.File(path, mimeType, fileDownloadName, enableRangeProcessing: true)`.
  - **MIME types:** `epub` application/epub+zip, `pdf` application/pdf, `mobi` application/x-mobipocket-ebook,
    `azw3`/`azw` application/vnd.amazon.ebook, `cbz` application/vnd.comicbook+zip, `cbr`
    application/vnd.comicbook-rar, anything else application/octet-stream.
  - **Download name:** `<Title> - <Author>.<ext>`, made filename-safe the way folder names are, instead of
    whatever the torrent called the file.
- A book or file not in the snapshot (not served, unlinked since, or a wrong index) gives 404. A file missing on
  disk also gives 404, and logs a warning.

### 4. Covers: `src/Fetchshelf.Data/Opds/BookCoverExtractor.cs` and `/opds/books/{hash}/cover`

- **EPUB:** read `META-INF/container.xml`, then the OPF. Take the manifest item with `properties="cover-image"`
  (EPUB 3), else the item named by `<meta name="cover" content="...">` (EPUB 2), else a manifest image whose id or
  href contains "cover". Read that entry from the zip.
- **CBZ:** the first image entry in name order.
- **PDF, MOBI, AZW3, CBR:** no cover in this issue (settled question 3).
- **Cache:** the extracted bytes go into a `covers` folder next to the database (beside `keys`), named by info
  hash and keyed on the book file's size and modification time. A re-placed file therefore gets a new cover, and
  the book is only opened once. Covers over 5 MiB are skipped, like MAM covers.
- **Serving:** `image/jpeg`, `image/png` or `image/webp` according to the entry, with a long `Cache-Control`.
  The same image is used for `image` and `thumbnail`. Resizing would need an imaging library, which isn't worth
  a dependency yet.
- **Feeds and covers:** a feed only links a cover once it is known to exist. Extraction happens on the first
  cover request, so the feed links covers for books with a cover-capable format (EPUB, CBZ), and the endpoint
  returns 404 if none is found. Readers handle a 404 cover by showing their own placeholder.

### 5. Endpoints and wiring: `src/Fetchshelf.Web/Opds/OpdsEndpoints.cs`

- **Registration:** `app.MapOpdsEndpoints()`, registered only when `settings.Opds` is set, so `/opds` is a plain
  404 otherwise. It follows `Themes.MapThemeEndpoint`.
- **Services:** the catalog reader is scoped (it uses the `DbContext`), the snapshot cache and the cover extractor
  are singletons, and the covers folder is resolved next to the database the way `keys` is in `Program.cs`.
- **Logging:** a debug line per feed request, and a warning for a missing file or an unreadable EPUB. Nothing at
  info level per request, because readers poll and page a lot.
- **Visible from the web UI:** the Config page's OPDS section shows the catalog URL to copy into a reader.

### 6. Documentation and examples

- `docs/configuration.md`: an `[opds]` section with the table, the environment variable and the validation rule.
- `docs/user-guide.md`: a section "Reading on an e-reader (OPDS)" covering:
  - turning it on and the catalog URL;
  - adding it in KOReader and one phone app;
  - what is served (ebook libraries only, the formats, covers);
  - no login, and the reverse-proxy note.

  The Contents list gets a link to it.
- `docs/config-examples/5-everything.toml`: the `[opds]` section with comments.
- `README.md`: the feature in the organizer bullet, and OPDS on the Untested list.

## Order of work

1. **Configuration:** `OpdsSettings`, loader validation, the environment variable, and the Config page section,
   with tests. Small, and nothing is served yet.
2. **Catalog:** `OpdsBook` and the grouping/sorting/search in Core, and `OpdsCatalogReader` in Data, with tests.
3. **Feed builder:** the Core feed functions, with tests on the XML.
4. **Endpoints:** feeds and downloads, with Web tests through `FetchshelfWebAppFixture`.
5. **Covers:** the extractor with its tests, the cache, and the endpoint.
6. **Docs.**
7. **Try with real readers:** KOReader and at least one of Thorium or Moon+ Reader, against the test library.

One feature branch and one PR, like Hardcover lists. Steps 1–3 could land on their own if the PR gets large.

## Risks

- **Reader quirks.** Readers differ in what they need: some require `rel="start"`, absolute `updated` dates, or
  a `length` on acquisition links. Testing against real readers (Step 7) is the only reliable check. Keep the
  feeds conservative: standard elements only, every recommended link.
- **Large navigation lists.** An author list with thousands of names, paged at 50, is tedious on an e-reader.
  If it proves unusable, a later step can add A–Z groups. Not needed to start.
- **Odd EPUBs.** Cover extraction has to survive broken zips and OPFs without crashing a request: any failure
  means "no cover", plus a warning once per book (the cache records the miss).
- **Exposure.** A catalog without login that serves files is only as safe as the network it is on. The user
  guide must say so plainly, as it does for the web interface.

## Verification

- **Core tests:**
  - feed XML: namespaces, required elements, the paging links on the first, middle and last page, entries with and
    without series, description, language and cover;
  - grouping and series order, including ranges and parts;
  - search on title, author and series.
- **Data tests:**
  - the catalog leaves out unlinked, replaced, metadata-only, audiobook-media-type and unserved-library torrents;
  - a book with several ebook parts lists them all;
  - the cover extractor on small generated EPUB 2 and EPUB 3 files, a CBZ, a broken zip and an EPUB without a cover.
- **Web tests:**
  - `/opds` is 404 without `[opds]`;
  - every feed returns its content type;
  - a download returns the file's bytes with the right type and supports a `Range` request;
  - an unknown hash or index is 404;
  - a crafted `LinkedFilesJson` path that leaves the folder is refused.
- **Scale:** a scale test that times `/opds/recent` and an author feed at 10,000 torrents, using the seeder
  from the scaling plan, to confirm the one-minute snapshot keeps paging fast.
- **By hand:** browse, search, download and open a book in KOReader and in one phone or desktop reader.
