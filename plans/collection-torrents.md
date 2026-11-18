# Split series collections into one library book each

> **Status: taken up again on 2026-11-18,** after being parked while MAM retired many collection torrents. Comic
> collections are still common: the Ultimate Spider-Man collection below is one. That example shows two gaps in the
> design, listed under "Collections without a series range" and to be settled before this plan is built.

## Context

A collection torrent holds several books of one series. The example that prompted this plan is MAM torrent 339727,
"Discworld" by Terry Pratchett (hash `939c95997a312c59cd9a2e2a89fb47aab0db5848`), with `series_info`
`{"1780": ["Discworld", "1-40", 1.0]}`. Its files are a Calibre-style export, one folder per book:

```
Terry Pratchett - Discworld 1-40/
  Mort (1019)/
    Mort - Terry Pratchett.epub
    cover.jpg
  Thud! (949)/
    Thud! - Terry Pratchett.epub
    cover.jpg
  … 40 folders in all
```

### What happens today

Fetchshelf treats every torrent as one book:

- **One record.** One `TorrentEntity` with the title "Discworld", series "Discworld #1-40", one `LibraryPath`, one
  `LinkedFilesJson`, one `AbsId`.
- **One folder.** `FolderNameComputer` gives `Terry Pratchett/Discworld/Discworld #1-40 - Discworld/`.
  `LinkFilePlacer` flattens every source sub-folder that isn't a disc folder, so the 40 epubs land side by side in
  it. The 40 `cover.jpg` files aren't linked (`jpg` isn't a listed format), and the folder gets one `metadata.json`
  describing the collection.
- **Readers see one book.** The OPDS catalog lists one entry, "Discworld", with 40 acquisition links
  (`OpdsBook.From` takes every linked file of the format). Tools that treat a folder as a book, such as
  Audiobookshelf, see one item.
- **Ownership is only partly known.**
  - The Follow job and the Series page understand the range: `TorrentSeries` rows keep `FirstNumber` and
    `LastNumber`, and an owned `1-40` covers books 1 to 40 (`FollowRunner.IsOwnedEntry`, `SeriesGaps`).
  - Selection's library check (`SelectionRunner`, matching on `NormalizedTitleKey`) and Hardcover lists
    (`HardcoverListImporter`, the same) only know the title "Discworld". A single "Mort" is not seen as owned and
    can be downloaded again.
  - The Clean job groups copies by title (`DuplicateCopies.GroupBySameBook`), so the six single Discworld books
    already in the library (#1, #2, #4, #8, #27, #32) are not seen as duplicates of the collection's copies. Both
    stay linked.

### Collections without a series range

The second example, from the dev instance on 2026-11-18: an Ultimate Spider-Man comic collection (hash
`f8185665d5a0c003f64b30eda4a66ec01212d78f`). MAM gives it no series at all, and its files are 145 numbered issues as
`cbr` beside one annual as `cbz`:

```
Ultimate Spider-Man/
  Ultimate Spider-Man 001.cbr … Ultimate Spider-Man 137.cbr   (145 cbr files in all)
  Annuals/Ultimate Spider-Man Annual 03.cbz
```

It was linked as one file: the annual, renamed after the collection, into the series layout's one-shots folder. Two
things went wrong, and the design below handles neither:

1. **The format is chosen before the split.** The Link job links only the files of the best format by `ebook_types`,
   and `cbz` ranks above `cbr`, so the one annual won over the 145 issues. In a collection, the formats are usually
   not copies of the same book but different books, so choosing one format for the whole torrent drops the rest. The
   choice has to be made per book group, or a collection has to link every ebook-side file.
2. **Detection needs a series range from MAM.** This torrent has none, so it wouldn't be taken for a collection.
   Comic collections often carry the series and the numbers only in their file names (`Ultimate Spider-Man 033.cbr`),
   and the series name in the folder name. Detection should also accept many files whose names share a series name
   and differ by a number, and take the series from them.

For a library with `layout = "series"`, a split collection's books are single files in the series folder, named
`Series #033 - Title` as single downloads are, rather than one folder per book.

### Goal

A collection whose files can be told apart is linked as if each book had come from its own torrent:
`Terry Pratchett/Discworld/Discworld #4 - Mort/Mort - Terry Pratchett.epub`, with that book's cover as `cover.jpg` and
its own `metadata.json` (and `metadata.opf` when the library asks for one). Every part of Fetchshelf that asks "is
this book owned" (selection, Hardcover lists, Follow, Upgrade, Clean, the Series page, OPDS, Audiobookshelf) sees the
books, not the collection.

## Open questions (defaults used unless decided otherwise)

1. **Layout.** Default: exactly as single downloads, so books from a collection and from single torrents sit side by
   side in the series folder, in the book layout and in the series layout alike.
2. **A book held both in a collection and as a single.** Default: Clean treats them as any two copies of a book:
   the better format wins, then the larger copy (`DuplicateCopies.PickKeeper`). The alternative is to always prefer
   the single, which has its own MAM description.
3. **Collections linked before this change.** Default: left as they are; **Relink** on the torrent's page splits
   one. No automatic re-split at startup.
4. **Scope.** Default: ebooks first. Audiobook collections use the same detection, but their series numbers mostly
   have to come from the description; they follow as a later step.

## Design

### Data model: one record per book, pointing at the collection

Each book of a split collection becomes its own `TorrentEntity`, with an id derived from the collection's hash,
`<hash>/<n>` (`n` is the book's place in the collection, from 1), in the way `MetadataOnlyTorrentId` derives
`mam:<id>`. A new `CollectionBookId` helper builds and parses these ids. Each book record has its own `Title`,
`NormalizedTitleKey`, `MetadataJson` (the collection's, with the book's title, series number, ISBN, description and
cover), `LibraryPath`, `LinkedFilesJson`, `AbsId`, and `TorrentPeople`/`TorrentSeries` rows. A new nullable column,
`CollectionTorrentId`, points at the collection's record.

The collection's own record stays: it is the one qBittorrent and MAM know (hash, `MamId`, category, client state,
removed from MAM). It has no `LibraryPath` of its own once split, and a new `CollectionBookCount` says how many book
records it has.

The reason for this shape: almost everything that works on library copies already works on `TorrentEntity` rows with
a `LibraryPath` (Clean, duplicates, the Series page, OPDS, the Audiobookshelf matcher, the Upgrade job, title-key
ownership checks). Book records get all of that unchanged. The collection's record, having no `LibraryPath`, drops
out of those queries by itself.

What has to learn about book ids is the code that uses a torrent's id as a qBittorrent hash or talks to MAM about it.
Each place maps a book id to its collection's hash:

- `QBittorrentConnectionResolver`, `ClientStatusChecker`, `DownloadProgressChecker`, `QBittorrentActions`;
- `Cleaner.RetireAsync`: `on_cleaned` and `on_unlinked` are applied to the collection's torrent only when its last
  linked book is retired or unlinked; until then the book is retired in the library only;
- the torrent page's qBittorrent actions (set category and tags, remove from qBittorrent) act on the collection, and
  are shown on the collection's page, not on a book's;
- `mark_removed` and Refresh read MAM for the collection's `MamId`; Refresh on a book refreshes the collection and
  re-derives the book.

### Detecting a collection

`CollectionSplitter` (Core, pure) decides from the metadata and the torrent's file list, after the Link job has
chosen the format:

- the torrent's series entry is a range (`SeriesEntryKind.Range`) of at least two books;
- the chosen-format files fall into at least two book groups:
  - **one group per source sub-folder** below the torrent's own top folder, when every file of the format sits in a
    sub-folder and no sub-folder is a disc folder (`LinkFilePlacer`'s disc pattern), or
  - **one group per file**, when the files of the format sit in one folder and there are as many as the range holds;
- every group gets a title (see below).

Anything else is linked as today, as one book. A range whose group count doesn't match it (39 folders for `1-40`) is
still split, as long as the groups are told apart; the books simply cover fewer numbers.

### Each book's details

From the collection, unchanged: authors, narrators, language, media type, category, tags, genres, flags.

Per book, from the first source that has it:

| Detail | Sources, in order |
| --- | --- |
| Title | The epub's own `dc:title`; the group's folder or file name, cleaned: a trailing ` (1019)` (a Calibre id), a trailing ` - <author>`, a leading series number. |
| Series number | The epub's own series number (`calibre:series_index`, or EPUB 3 `belongs-to-collection` with `group-position`), when its series name is the collection's; a leading number in the folder or file name (`04 - Mort`); a numbered list in the MAM description, matched by title key, a truncated title matching by prefix ("The Amazing Maurice and His Educated Rod" → "… Rodents"); a book with the same title key and series already in the library. None found: no number, and the folder is `Discworld/Mort`. |
| ISBN | The epub's `dc:identifier` with the ISBN scheme. |
| Description | The epub's `dc:description`. Not the collection's, which describes the collection. |
| Cover | An image in the book's source folder (`cover.jpg`, `cover.png`, or the only image), linked as `cover.jpg`. |

A Hardcover lookup by ISBN could fill in numbers and descriptions too, but the Hardcover client only reads the
user's own lists today; that is left for later.

### Linking and relinking

`LinkRunner.ProcessFinishedTorrentAsync` and `RelinkCoreAsync` call the splitter after choosing the format. For a
collection they create or update one book record per group and place each with the existing `PlaceFiles` path, as
for a single torrent: its own folder from `FolderNameComputer`, its files through `LinkFilePlacer`, its
`metadata.json`/`metadata.opf`, its cover. The collection's record keeps no `LibraryPath`.

- **Events:** one Linked event per book, with the book's id, so the Events page, notifications and the Jobs page
  count books. The `linked` notification merges them as it already merges bursts.
- **Relinking a collection linked before this change** removes the old single folder (with the empty-parent removal
  from #29) and places the books. A book record that no longer matches a group after a relink (the files changed) is
  unlinked.
- **A book folder that already holds a single copy of the same book:** both copies are placed, as two singles of one
  book are today (numbered names on clashes), and the Clean job decides between them (open question 2).

### Pages

- **Torrents page:** book records are listed like any torrent, with a "from collection *Discworld*" note linking to
  the collection. The collection's row shows "Collection of 40 books" instead of a library status.
- **Torrent page of a collection:** its books, each with its folder and status, and the qBittorrent actions.
- **Torrent page of a book:** a link to its collection; no qBittorrent actions; Unlink and Relink act on the book.
- **Duplicates page:** a book from a collection and a single of the same book show as copies, as two singles do.

## Steps

Each step keeps `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` passing. All of it goes on one
feature branch and one pull request into `main`.

1. **`CollectionSplitter` and the book details** (Core), with tests built from real layouts: the Discworld export
   (folders with Calibre ids, covers, a numbered description list with one truncated title), a flat folder of
   numbered files, an audiobook with `Disc N` folders (not split), a range with fewer groups than books, and a
   torrent without a range (not split). Reading an epub's OPF goes in a small reader with its own tests.
2. **Data:** `CollectionTorrentId` and `CollectionBookCount` columns (migration), `CollectionBookId`, and the
   projection rows for book records.
3. **Link job and relink:** splitting, per-book placement, events.
4. **qBittorrent and MAM mapping:** every place listed under the data model; `on_cleaned`/`on_unlinked` only for
   the last book.
5. **Pages:** Torrents list, torrent pages, Duplicates.
6. **Docs:** the user guide (linking, Clean, Unlink, collections), the configuration reference if any setting comes
   with it (none planned), and the release notes' **Upgrading**: collections linked earlier stay as they are until
   relinked.

Later, outside this plan: audiobook collections; a Hardcover ISBN lookup for numbers and descriptions; selection
passing over a collection whose books are all owned already.

## Verification

- Unit tests for the splitter and the book details, as listed in step 1.
- `LinkRunnerTests`: a finished collection is linked as one folder per book with covers and metadata, and its records
  point at the collection; relinking a collection linked the old way splits it and removes the old folder.
- `CleanerTests`: a single and a collection's copy of one book are grouped, and the worse one is retired; retiring
  or unlinking a book applies `on_cleaned`/`on_unlinked` only when it is the collection's last linked book.
- `SelectionRunner`/`HardcoverListImporter` tests: a book held only through a collection counts as owned.
- On the live instance, after a release: relink torrent 339727, then check the folders in
  `/data/fs-ebooks/Terry Pratchett/Discworld/`, the OPDS catalog (40 books under the series, in order), the Series
  page, and the Duplicates page for the six books also held as singles.
