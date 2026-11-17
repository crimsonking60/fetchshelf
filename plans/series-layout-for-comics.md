# A series layout for comic and manga libraries — implementation plan

## Context

`FolderNameComputer.Compute` gives every torrent its own folder, `Author/Series/Series #n - Title`, and
`LinkRunner.PlaceFiles` writes `metadata.json` (and `metadata.opf` when asked) into it. That suits Audiobookshelf,
which treats a folder as a book. Comic servers read a library the other way round. Komga
([scanning guide](https://komga.org/docs/guides/scan-analysis-refresh)):

- creates a **series for every folder that holds files**, at any depth; each file in it is a book of that series, and
  the folders above it are not represented;
- takes the series title from the folder name, unless a Mylar `series.json` in the folder or a `ComicInfo.xml` inside
  the archive says otherwise;
- takes book numbers from `ComicInfo.xml`'s `Number`, and orders by file name without it;
- treats loose files in a configured one-shots folder (`_oneshots` in its example) as standalone books
  ([one-shots guide](https://komga.org/docs/guides/oneshots)).

Three volumes of Saga from three torrents are linked today as

```
comics/Brian K. Vaughan/Saga/Saga #1 - Saga, Volume 1/Saga v01 (2012) (Digital).cbz
comics/Brian K. Vaughan/Saga/Saga #2 - Saga, Volume 2/Saga v02 (2013) (Digital).cbz
comics/Brian K. Vaughan/Saga/Saga #3 - Saga, Volume 3/saga_003.cbz
```

and Komga shows three series of one book each, titled `Saga #1 - Saga, Volume 1` and so on. On top of that:

- **The author level is unreliable for comics.** MAM lists writers and artists together, and writers change within a
  long series, so one series can be spread over several author folders.
- **The sidecars are not read.** Komga reads neither `metadata.json` nor `metadata.opf`.
- **Metadata can't go into the archive.** `ComicInfo.xml` inside the cbz is Komga's main source, but with hardlinks
  changing the archive changes the file being seeded. Folder names, file names and a `series.json` next to the files
  are what can be written without touching the download.

`plans/safe-folder-names.md` plans a `folder_template`, but it refuses a template without `{title}` (two books would
share a folder) and keeps one folder per book, so it does not help here. A series layout is a different shape, not a
different template.

Part of what a shared folder needs exists already: `LibraryFileRemover.Remove` deletes only the files a torrent
recorded, leaves files another torrent in the folder lists (`FolderUseByOthersAsync`), keeps the extras while the
folder is shared, and removes empty parents. Two formats of one book share a folder that way today.

### Goal

A library can be set to the series layout:

```
comics/Saga/Saga #001 - Saga, Volume 1.cbz
comics/Saga/Saga #002 - Saga, Volume 2.cbz
comics/Saga/Saga #003 - Saga, Volume 3.cbz
comics/Saga/series.json
comics/_oneshots/Some Standalone Graphic Novel.cbz
```

Komga then shows one series, Saga, with three books in order. Everything that works on library copies today (Clean,
Duplicates, Upgrade, OPDS, Unlink, Relink, the mismatch flags) keeps working.

## Open questions (defaults used unless decided otherwise)

1. **Torrents with several files of the chosen format** (packs, or a volume split into chapters). Default: they keep
   their own file names, flattened as today by `LinkFilePlacer`, since their numbering can't be told apart until
   `plans/parked/collection-torrents.md` is built. They sit in the series folder beside renamed singles; their order in
   Komga follows the pack's own names.
2. **Audiobooks in a series library.** Default: a series library links only the ebook-side format; a torrent with no
   ebook-side file gets a Link error (*This library's series layout links only ebook formats*). Comics and manga are
   what the layout is for, and a folder of mp3s from several torrents is no use to anyone.
3. **Torrents without an author.** Default: the series layout doesn't need one, so they are linked. The book layout
   keeps refusing them.
4. **Two different series with the same name** (`Batman` from 1940 and from 2016). Default: they share a folder.
   **Edit** can rename one series (`Batman (2016)`); a `{series_year}`-style disambiguation is left for later.

## Settings

Per `[[library]]`:

| Setting | Default | Description |
|---|---|---|
| `layout` | `"book"` | `"book"`: one folder per book, as today. `"series"`: one folder per series, holding every book of the series, for comic servers such as Komga. |
| `oneshots_dir` | `"_oneshots"` | Series layout only: the folder under `library_dir` that books without a series go into. Komga's One-Shots directory setting must name the same folder. |
| `series_json` | `true` | Series layout only: write a Mylar `series.json` into each series folder. |

Validation at load, each naming the library:

- `oneshots_dir` or `series_json` set on a library whose `layout` is `"book"`.
- `metadata_json = true` or `metadata_opf = true` set on a series library: one per-book file can't sit in a folder
  shared by many books. The global `metadata_json` and `metadata_opf` don't apply to a series library, so the default
  `metadata_json = true` doesn't make an existing config invalid.
- `oneshots_dir` empty, containing `/` or `..`, or not a valid folder name.
- Once `plans/safe-folder-names.md` lands: `folder_template` together with `layout = "series"`.

The Config page and `--check-config` show the three settings with the library's others (`ConfigSummary`).

## Design

### Names (Core, pure)

`Fetchshelf.Core/Organizer/SeriesLayoutNamer.cs`, next to `FolderNameComputer`, using the same sanitizing:

- **Folder.** The series chosen as `FolderNameComputer.SelectSeries` chooses it (the first with entries, else the
  first): `<library_dir>/<Series>`. Without a series: `<library_dir>/<oneshots_dir>`.
- **File name**, for a torrent with exactly one file of the chosen format:
  - with a series and entries: `<Series> #<entries> - <Title>[, <Edition>]<ext>`;
  - with a series and no entries: `<Series> - <Title>[, <Edition>]<ext>`;
  - without a series: `<Title>[, <Edition>]<ext>`.
- **Padded numbers.** The whole-number part of each entry is padded to three digits, so name order is series order
  past book 9 and past book 99: `1` → `001`, `1.5` → `001.5`, `1-6` → `001-006`, a part `2p1` → `002p1`. Several
  entries are joined as today (`001, 002`). Numbers of 1000 and up are left as they are.
- Narrators are left out: they don't apply to comics, and `exclude_narrator_in_library_dir` has no effect here.

`SeriesLayoutNamer.Compute(libraryDir, oneshotsDir, metadata)` returns the folder and the file base name; the file
name is used only when one file is placed.

### Linking (`LinkRunner`)

`PlaceFiles` branches on the library's layout:

- **Format.** For a series library `ChooseFormats` takes only the ebook side; no ebook-side file is the Link error
  from open question 2.
- **Destination.** One chosen file goes to `<folder>/<file base name><ext>`; several keep `LinkFilePlacer.DestinationPaths`
  under the series folder. `FileLinker.Link` already handles a clash with another torrent's file: the same file counts
  as already placed, a different one gets `name (2).ext`, and `LinkedFileRecord` stores the name actually used.
- **Sidecars.** No `metadata.json` or `metadata.opf`. With `series_json`, `series.json` is written (or rewritten)
  into a series folder; none in the one-shots folder.
- `RecordPlacement` stores the series folder as `LibraryPath`, so every torrent of a series has the same one.

`series.json` (`Fetchshelf.Core/Organizer/MylarSeriesJsonBuilder.cs`) is Mylar's format, `{"version": ..., "metadata":
{...}}`, with what Fetchshelf knows: `name` (the series name, unsanitized, so a `:` lost in the folder name comes
back), `type` and `booktype` from the media type, and empty or null values for the rest. Komga reads `name`,
`status`, `description_text`/`description_formatted`, `publisher`, `age_rating`, `total_issues`, and `volume` with
`year` for a title suffix. Before writing the builder, the fields Komga's parser requires to be present are checked
against Komga's source, and every one of them is written, so a missing field doesn't make Komga skip the file.
`volume` is written as `1`, which adds no year to the title.

### Removing (`LibraryFileRemover`, `Cleaner`)

`series.json` joins `cover.jpg`, `metadata.json` and `metadata.opf` in `Extras`, so it goes with the last book of the
folder and stays while others use it. Retiring a lesser format, Unlink and Relink already go through
`FolderUseByOthersAsync`, so a series folder's other books are left alone. Tests pin this for a series folder of
three torrents.

### Mismatch flags and relinking

`SyncMismatch` computes the expected place with `SeriesLayoutNamer` for a series library. Besides the folder, it
compares the single linked file's name with the expected one, so a corrected title or number shows as *Different
folder* (the existing state, with its existing **Relink** fix) and not as nothing. A torrent with several files is
compared by folder only. `RelinkCoreAsync` needs no change beyond using the new names: old files out, new files in,
with the shared-folder rules above.

Switching a library between layouts flags every book in it *Different folder*; **Relink selected** moves them. The
book layout's per-book folders are removed with their last file as today.

### Other users of `LibraryPath`

- **`AbsMatcher`** skips torrents in a series library: Audiobookshelf treats a folder as one item, so every book of a
  series would match the same item. The docs say a series library isn't meant for Audiobookshelf.
- **OPDS, Upgrade, Clean, Duplicates, the Series and Following pages** work per torrent with its `LinkedFilesJson`,
  and need no change. Tests check OPDS serves each book of a shared folder once, with its own file.
- **Torrent page.** The library folder line shows the series folder and the file name, since the folder alone no
  longer says which file is the book.

## Steps

Each step keeps `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` passing. All of it goes on one
feature branch and one pull request into `main`.

1. **Settings:** `layout`, `oneshots_dir`, `series_json` on `Library`, the `ConfigLoader` bindings and validation,
   `ConfigSummary`. `ConfigLoaderTests` for every rule above, including an existing config with the global
   `metadata_json = true` and a new series library loading cleanly.
2. **Names:** `SeriesLayoutNamer` and `MylarSeriesJsonBuilder` with `SeriesLayoutNamerTests` and
   `MylarSeriesJsonBuilderTests`.
3. **Linking and removing:** `PlaceFiles`, `ChooseFormats`, the no-author rule, `Extras`, `SyncMismatch`, `AbsMatcher`.
4. **Pages:** the torrent page's folder line.
5. **Docs:** `docs/configuration.md` (`[[library]]` settings, "Folder naming" gets a series layout part with the
   example above) and `docs/user-guide.md` (the comics example gets `layout = "series"`, and a short "Komga" section:
   point a Komga library at `library_dir`, set its One-Shots directory to `oneshots_dir`, and why no `ComicInfo.xml`
   is written). `RELEASES.md` under the next version, with **Upgrading**.

## Tests

- `SeriesLayoutNamerTests`: series with one entry, several entries, a decimal, a range, a part, no entries; no
  series (one-shots folder); an edition; a narrator left out; padding at 9/10, 99/100 and 1000; characters the
  sanitizer removes.
- `MylarSeriesJsonBuilderTests`: the series name unsanitized, `volume` 1, every field Komga requires present.
- `LinkRunnerTests`:
  - three single-file torrents of one series land as three renamed files in one folder with one `series.json`;
  - a series-less torrent lands in the one-shots folder without `series.json`;
  - a pack keeps its own file names beside them;
  - a clash with another torrent's different file gets `(2)`;
  - an audio-only torrent gets the Link error;
  - a torrent without an author is linked;
  - an edited title flags *Different folder*, and relinking renames the file and leaves the other books alone;
  - switching a library to `layout = "series"` flags its books and relinking moves them.
- `CleanerTests`: retiring or unlinking one book of a shared series folder removes only its file; the last one takes
  `series.json` and the folder with it.
- `AbsMatcherTests`: a torrent in a series library is skipped.
- `OpdsCatalogReaderTests`: two books in one series folder are served as two entries, each with its own file.
- Manual, on the dev instance (:23157): a comics library with `layout = "series"`, a few volumes of one series and a
  one-shot linked, then a Komga container pointed at it (One-Shots directory `_oneshots`): one series with its books
  in order, its title from `series.json`, the one-shot standalone.

## Release notes

- Added: `layout = "series"` on a `[[library]]` links comics and manga into one folder per series, with
  `Series #001 - Title` file names and a Mylar `series.json`, the layout Komga reads. Books without a series go into
  `oneshots_dir`.
- Upgrading: nothing to do. Libraries keep today's layout unless they set `layout = "series"`; then their books are
  flagged *Different folder*, and **Relink selected** on the Torrents page moves them.

## Out of scope

- Writing `ComicInfo.xml` into archives: it changes the seeded file. Possible later for libraries with
  `method = "copy"` only.
- Covers: Fetchshelf writes none today; series and book artwork in Komga's local-artwork names can follow
  `plans/covers-from-mam.md`.
- Splitting packs into numbered books: `plans/parked/collection-torrents.md`.
- A series layout for Kavita: its expected `Series/files` shape is close, but it hasn't been checked; the docs
  mention only Komga until it is.
- Reading direction for manga: Komga takes it from `ComicInfo.xml` only.
