# File-based lists: wanted books from a text or CSV file — implementation plan

## Context

Hardcover is the only source of wanted books. `[[hardcover_list]]` reads a reading status or list from Hardcover,
keeps one `ListItemEntity` per book, searches MAM for each with the list's `grab` blocks, and shows the result on
the Lists page. Everything after "read the books" is not Hardcover-specific:

- `HardcoverListImporter.RunAsync` gets the books from `IHardcoverBookSource.GetBooksAsync`, then syncs the
  items, takes `MamAccessGate`, works out the budget and searches.
- `HardcoverBook` (id, title, slug/URL, authors, series, cover) is all the importer reads.
- `ListEntity.ListType` already names the service ("Hardcover"), and the Lists pages show whatever is stored.

A file is the simplest second source, and covers every service that can export a list: Goodreads and StoryGraph
exports, a spreadsheet, or a list typed by hand. It needs no API and no account.

## Change

### 1. A source-neutral importer

- `ListBook` in `Fetchshelf.Data.AutoDownloader`: `ExternalId`, `Title`, `Authors`, `Series` (name and
  optional number), `CoverUrl`, `BookUrl`. `HardcoverBook` maps to it (`ExternalId` = the Hardcover id).
- `IListBookSource` replaces `IHardcoverBookSource`: `Task<IReadOnlyList<ListBook>> GetBooksAsync(ct)`, one
  instance per configured list, so the source carries its own settings.
- `ListDefinition`: what the importer needs from a list's config, whatever its source: `ListId`, `DisplayName`,
  `ListType`, `PreferFormat`, `Category`, `Grab`, `DryRun`, and the error stage to record under.
  `HardcoverListEntry` and the new `FileListEntry` each produce one.
- `HardcoverListImporter` becomes `ListImporter`, taking a `ListDefinition` and an `IListBookSource`. Its logic is
  unchanged; the log lines say "List {Name}" instead of "Hardcover list {Name}".
- `HardcoverListRunSummary` becomes `ListRunSummary`; `JobOutcomes.HardcoverList` becomes `JobOutcomes.List`.
- In `Program.cs` the importer is registered whenever any list is configured, not only with `[hardcover]`.

This is a rename and a seam, and is its own commit: the Hardcover tests keep passing with only names changed.

### 2. Configuration

```toml
[[file_list]]
name = "Goodreads to-read"
path = "lists/goodreads_library_export.csv"   # relative to the config file's folder
shelf = "to-read"                              # optional, see below
prefer_format = "audio"
category = "audiobooks"
search_interval = 60

[[file_list.grab]]
cost = "free"
```

- `path` is required. A relative path is resolved against the folder the config file is in, so under Docker a
  file next to `config.toml` in `/config` is found as `path = "my-list.csv"`.
- `prefer_format`, `category`, `grab`, `search_interval`, `dry_run` and `name` mean what they mean in
  `[[hardcover_list]]`.
- `shelf` keeps only the rows on one shelf, for exports that hold every shelf: the `Exclusive Shelf` column of a
  Goodreads export, the `Read Status` column of a StoryGraph one. Ignored, with a warning at load, for a file
  without such a column.
- Job name: the `name`, else `file_list[<index>]` (`AutoDownloaderJobNames.FileList`), unique among all jobs like
  the others.
- The list id is `file:<job name>`, so renaming the file keeps the list's items, and renaming the list starts a
  new one (as with Hardcover).
- `ConfigLoader` checks the shape; `--check-config` also checks that the file exists and can be read, and says how
  many books it found. At startup a missing file isn't an error: it may be put there later, and the job fails
  until it is.

### 3. Reading the file

`ListFileReader` in `Fetchshelf.Core/Lists/`, pure (takes the text, returns books and problems), picks the format
from the content:

**CSV** (the first line has a `Title` column; comma or semicolon separated, quoted fields, UTF-8 with or without
BOM). Columns are found by header name, case-insensitively:

- `Title` (required).
- Authors: `Author` plus `Additional Authors` (Goodreads), or `Authors` (StoryGraph, comma separated), or
  `Author`/`Authors` in a hand-made file (`;` or `&` separated as well).
- Series: `Series` and `Series Number`/`Number` columns when present. Goodreads puts the series in the title,
  `Title (Series Name, #3)`; that form is split off the title with the existing `SeriesParser`.
- `ISBN`/`ISBN13`/`ISBN/UID`: stored for a stable id (below); MAM search can't use it.
- `Shelf`/`Exclusive Shelf`/`Read Status`: for `shelf`.

**Plain text**: one book per line, `Title by Author` or `Title - Author`; the author is optional. Blank lines and
lines starting with `#` are skipped.

Each book's `ExternalId` is its ISBN when it has one, else a key of its title key and first author key
(`TitleNormalizer`, `PersonNameKey`). So the same book stays the same item when the file is re-exported or
reordered, and its order in the file is its `Position`. Two rows with the same id are one book.

A row that can't be read (no title, an unclosed quote) is skipped and reported: the run's outcome says "2 lines
could not be read", and the log names the line numbers. The file is read on every run; nothing is cached.

### 4. Lists page and errors

- A file list shows on the Lists page like a Hardcover one, with "File" as its source and the file name. Books
  have no cover or link.
- Book errors are recorded under a new `FileList` error stage, so the Errors page names the source.
- A file that is missing or unreadable fails the run with "List file not found: lists/x.csv"; the job-failure
  handling already shows that on the Jobs and Errors pages and can notify.

## Tests

- `ListFileReaderTests` (Core):
  - a Goodreads export (the real header line, a title with a series in parentheses, additional authors, the
    `Exclusive Shelf` column);
  - a StoryGraph export (comma-separated authors, `Read Status`);
  - a hand-made CSV with semicolons, quoted commas and a BOM;
  - plain text with "by", " - ", no author, comments and blank lines;
  - a broken row skipped and reported with its line number;
  - the same book twice; the id stable when rows are reordered; ISBN preferred for the id.
- `ListImporterTests`: the existing Hardcover importer tests, renamed, plus one with a file source showing the
  same behavior.
- Config tests: `[[file_list]]` defaults, `path` required, relative paths resolved against the config folder,
  `shelf`, unknown keys rejected; `--check-config` with a missing and an existing file.
- Lists page test: a file list's items shown with their statuses.

## Documentation

- `docs/user-guide.md`: "Books you want, from a file" after the Hardcover section, with how to export from
  Goodreads and StoryGraph (the menu names only, no screenshots) and a plain-text example.
- `docs/configuration.md`: the `[[file_list]]` block.
- `example-config.toml`: a commented-out `[[file_list]]`.
- `docs/config-examples/`: a small example list file.

## Release notes

- Added: `[[file_list]]` reads wanted books from a CSV or text file, such as a Goodreads or StoryGraph export, and
  searches MAM for them like a Hardcover list. They show on the Lists page.
- Upgrading: nothing to do. The Hardcover list jobs' log lines now start "List" instead of "Hardcover list".

## Out of scope

- Audiobookshelf series and MAM bookmarks as list sources.
- Reading a list from a URL.
- Searching MAM by ISBN.
- Writing back to the file (marking a book found).
