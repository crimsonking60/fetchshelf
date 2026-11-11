# Queryable authors and series: the library's people and series as rows — implementation plan

## Context

A torrent's authors, narrators, series, language and media type live only inside `TorrentEntity.MetadataJson`.
Anything that asks "which books by this author do I have?" or "which numbers of this series are owned?" has to
load every row and deserialize its JSON. The Torrents page already does this for its Language and Media type
filters and their dropdowns (`Pages/Torrents/Index.cshtml.cs`, `LoadAsync`), and the features planned after this
one need the same questions answered on every run:

- series completion and author follow (`plans/series-and-author-follow.md`) need the owned entries of a series and
  the books of an author;
- the library explorer (`plans/library-explorer.md`) browses by author and series;
- the scalability plan's step 7 (`plans/done/scalability-10k-torrents.md`, still open) needs `Language` and
  `MediaType` as columns to filter the Torrents page in SQL.

`MetadataJson` is written in many places: `LinkRunner`, `SelectionRunner` (refreshing a library entry, creating a
metadata-only entry), `SnatchlistImporter`, `DownloadRunner`, the Search page's import, the Errors page's
restore, and `TitleMaintenanceRunner`. A projection that each of these has to remember would drift.

This plan makes authors, narrators and series queryable rows, and `Language` and `MediaType` columns, kept in sync
in one place. It adds no new feature beyond author and series filters on the Torrents page, which are the first
user of the new rows and prove them out.

## Change

### 1. The rows

Two side tables, one row per name, both deleted with their torrent (`ON DELETE CASCADE`):

- `TorrentPeople`: `TorrentId`, `Role` (`Author` or `Narrator`), `Position` (the order in the metadata), `Name`
  (as written), `NameKey` (`PersonNameKey.Of(Name)`). An identity primary key, so replacing a torrent's rows in one
  save never collides with the rows being deleted; indexes on `TorrentId` and `(Role, NameKey)`.
- `TorrentSeries`: `TorrentId`, `Position`, `Name`, `NameKey`, `Kind` (`Number`, `Part` or `Range`),
  `FirstNumber`, `LastNumber` and `Display`. An identity primary key; indexes on `TorrentId` and `NameKey`. The
  numbers are stored as doubles: SQLite keeps decimals as text, which can't be compared in SQL.
  - A series without a number gets one row with the numbers null, so "every book in this series" still finds it.
  - A series with several entries (`1, 3` or `1-3`) gets one row per entry; `Position` numbers them across the
    torrent's series.
  - `NameKey` is folded the way title keys are (`TitleNormalizer.Normalize`), so case, punctuation and
    diacritics don't split a series in two. A blank key is never written; such a series is left out.

`SeriesEntry` gains a `LastNumber`: the second number of a range, the number itself otherwise. Today a range
keeps its second number only inside `Display`, so `LastNumber` is derived from `Display` and never stored: the
metadata JSON is unchanged, and older JSON reads the same. A part (`2p1`) counts as its book's number.

`TorrentEntity` gains two columns, both indexed:

- `Language` (the `Language` enum's name, as the Torrents page compares it today);
- `MediaType` (the `MediaType` enum's name).

These are the same two columns the scalability plan's step 7 asks for; that step then only adds `SearchText` and
the SQL paging.

The side tables are for `Torrents` only. The selection queue (`SelectedTorrents`) is small and is read whole
already; the features that need "selected but not yet downloaded" read it as they do today.

### 2. Kept in sync in one place

`FetchshelfDbContext` overrides `SaveChangesAsync` (and `SaveChanges`):

1. It detects changes once, and the save itself doesn't detect them again.
2. It collects every `TorrentEntity` entry that is `Added`, or `Modified` with `MetadataJson` among its modified
   properties.
3. For each, it deserializes `MetadataJson` once, sets `Language` and `MediaType`, and adds the new rows.
4. For the modified ones, it deletes the old rows by torrent (`ExecuteDelete`), not row by row, in one transaction
   with the save. Two saves of one torrent at the same time therefore both succeed, and the last one wins, as
   before. A failed save rolls the delete back and keeps the old rows. Rows made for a save that failed are dropped
   before the next save, so a retry makes no duplicates, and leaves no stale rows when the metadata is no longer
   changed.

The projection itself, `TorrentProjection.From(TorrentMetadata) → (people, series, language, mediaType)`, is a
pure function in `Fetchshelf.Core/Metadata/` and is where the rules above live and are tested.

None of the write sites change. A write that sets `MetadataJson` is projected, whichever page or runner made
it. Metadata that can't be read or projected is logged and the torrent gets no rows, rather than failing the save or
the startup upgrade: the JSON was written by Fetchshelf itself and has always been readable, but a save must not be
blocked by a side table. Nulls in stored JSON (an author list of null) are skipped like blanks.

Bulk paths that clear the change tracker (`LinkRunner`'s batches) are unaffected: the projection runs per
`SaveChanges`, on what that save contains.

### 3. Filling the rows for an existing database

`DatabaseMigrator` gets data version 2:

- an EF migration adds the tables and the two columns, empty;
- the data upgrade reads the torrents in batches of 500, projects each, and inserts the rows and sets the two
  columns with one `SaveChanges` per batch. The metadata JSON is read, never written back;
- a database at version 1 is upgraded at startup like the title keys were. At 10,000 torrents this is expected
  to take seconds; the log says how many torrents were projected and how long it took.

### 4. Query helpers

`LibraryQueries` in `Fetchshelf.Data` (a `Library` namespace would clash with the `Library` config class), each a
query that never loads `MetadataJson`:

- `PeopleAsync(role)`: every author (or narrator) key with its display name (the most used spelling) and book
  count.
- `SeriesAsync()`: every series key with its display name, the owned entries (numbers and display forms) and
  media types. "Owned" means linked or metadata-only and not replaced (`ReplacedWithTorrentId == null`).
- `TorrentIdsWithPerson(role, nameKey)` and `TorrentIdsInSeries(nameKey)`, as queries to combine with others.
- `PersonNameAsync(role, nameKey)` and `SeriesNameAsync(nameKey)`: the name a key stands for.
- `OwnedEntriesAsync(seriesKey, mediaSide)`: the numbers owned in one series, as audiobook or ebook, which
  series completion needs.

### 5. First use: the Torrents page

- The Library, qBittorrent category, Language and Media type filters run in SQL on the columns, and their
  dropdown options are `SELECT DISTINCT`s. Status, problems, the search text and the sort stay in memory until
  the scalability plan's step 7, on the rows the SQL filters leave.
- Three new filters, `Author`, `Narrator` and `Series`, take a name key. The author and series names in the list,
  and the author, narrator and series names on the torrent detail page, link to the Torrents page filtered by
  them. A filter in use shows above the list with the display name, not the key, and an × to drop it.

## Tests

- `TorrentProjectionTests` (Core): authors and narrators keep their order; an author listed twice gives one row;
  a series without a number, with one number, with `1, 3`, with a range and with a part; a blank series name is
  left out; series names that differ in case or punctuation get the same key; language and media type names.
- `SeriesParserTests`: `LastNumber` for numbers, ranges and parts; old JSON without `LastNumber` reads the same.
- `FetchshelfDbContext` projection tests against SQLite (not the in-memory provider, so the cascade and the
  transaction are real):
  - adding a torrent writes its rows and columns;
  - changing `MetadataJson` replaces the rows; changing another column leaves them alone;
  - deleting a torrent deletes its rows;
  - a failed save leaves the old rows in place;
  - unreadable JSON saves the torrent with no rows and logs a warning.
- Data upgrade test: a database at data version 1 with a few torrents gets their rows and columns, and ends at
  version 2; running it again changes nothing.
- `LibraryQueriesTests`: authors and series with counts, owned entries per media side, replaced copies left out.
- Torrents page tests: the Author, Narrator and Series filters and their links, and the Language and Media type
  filters giving the same rows as before.

## Documentation

- `docs/user-guide.md`, Torrents page: the Author, Narrator and Series filters, and the links to them.
- `docs/contributors-guide.md`: the side tables, that they are derived from `MetadataJson` in
  `SaveChangesAsync`, and that nothing else writes them.
- `plans/done/scalability-10k-torrents.md` is not edited; when its step 7 is done, it uses these columns.

## Release notes

- Added: Author, Narrator and Series filters on the Torrents page, reached by clicking a name in the list or on a
  torrent's page.
- Upgrading: the first start after upgrading fills the new author and series tables from the existing torrents.
  This takes a few seconds for a large library and needs nothing done.

## Out of scope

- A Series or Authors page: series completion and author follow add one.
- `SearchText` and SQL paging for the Torrents page: the scalability plan's step 7.
- Side tables for `SelectedTorrents` or `DuplicateTorrents`.
- Merging spellings of one author or series by hand (aliases).
