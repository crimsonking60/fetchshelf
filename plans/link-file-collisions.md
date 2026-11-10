# Linking: files with the same name must not be merged or deleted — implementation plan

## Context

The Link job places each chosen file of a torrent in the book's folder with `LinkFilePlacer.DestinationPath`
(`src/Fetchshelf.Data/Organizer/LinkFilePlacer.cs`): a source sub-folder named like `CD 1`, `Disc 2` or `Disk 3`
becomes `Disc N`, and **every other sub-folder is flattened**, so only the file name is kept. `FileLinker.Link`
then returns `AlreadyExists` whenever the destination exists, **without looking at what is there**, and
`LinkRunner.PlaceFiles` records the file as linked either way. Three things go wrong:

1. **Within one torrent.** `Part 1/01.mp3` and `Part 2/01.mp3` both go to `<folder>/01.mp3`. The second is not
   placed, but it is recorded as linked and the torrent is reported as linked. The library copy is missing files,
   and nothing says so.
2. **Across torrents sharing a folder.** The folder name comes from the metadata (author, series, title, edition,
   narrator) and not the format or language, so two torrents of one book land in one folder: an `m4b` and an `mp3`
   copy, two `epub` uploads, or an English and a German edition whose titles agree. If both contain `Book.epub`,
   the second torrent's record lists a file that is physically the first torrent's link.
3. **Deleting the other torrent's file.** When the Clean job retires one of those copies, or a relink moves it,
   `LibraryFileRemover.Remove` deletes every file in its record. `IsFolderSharedAsync` only protects `cover.jpg`,
   `metadata.json` and `metadata.opf`, so in case 2 it deletes the file the kept copy needs, while the kept copy's
   record still lists it.

A partly written copy has the same shape: if Fetchshelf stops while `File.Copy` runs, the next run finds the
destination and accepts it as `AlreadyExists`.

## Change

### 1. Plan the destinations before placing (within one torrent)

`LinkFilePlacer` gets a pure function that places all of a torrent's chosen files at once:

```csharp
public static IReadOnlyList<string> DestinationPaths(string destinationFolder, IReadOnlyList<string> sourceRelativeFilePaths)
```

- Each file gets today's destination (`Disc N` rule, otherwise flattened), so torrents without a clash are placed
  exactly as before and existing libraries don't change.
- When two or more files would get the same destination (compared ignoring case, since libraries are often read
  over SMB or from Windows and macOS), those files keep their source sub-folders, **flattened into the file name**:
  `Part 1/01.mp3` becomes `Part 1 - 01.mp3`, `Part 2/01.mp3` becomes `Part 2 - 01.mp3`. The torrent's top-level
  folder (the torrent name) is left out, and a `Disc N` folder still becomes a folder. Prefixing keeps one folder
  per book, which Audiobookshelf reads as one item; separate sub-folders could be scanned as separate books. The
  prefix sorts the files by part, then by their own name, which keeps the playing order.
- If prefixing still leaves a clash (two files with identical relative paths can't exist in one torrent, so this
  is only reachable through case differences), the later one gets ` (2)`, ` (3)` before the extension.
- `PlaceFiles` uses this instead of calling `DestinationPath` per file.

### 2. Check what is already there (across torrents, and partial copies)

`FileLinker.Link` stops treating an existing destination as done without looking:

- **Same file:** the destination has the source's size, and for a symlink it points at the source. Then the
  outcome stays `AlreadyExists`. This is the case for a relink into the same folder, a run after a database restore,
  or a link run that was interrupted after this file.
- **Different file:** anything else. The file is placed under a free name instead: `Book (2).epub`, `Book (3).epub`.
  Each numbered name is checked the same way, so a torrent placed as `Book (2).epub` earlier is found there again on
  a relink instead of getting `Book (3).epub`. `FileLinker.Link` now returns a `LinkResult` (what it did, and the
  path the file ended up at), and the record holds the name actually used.
- **Source missing:** with nothing to compare, an existing destination still counts as placed, as before. Both books stay
  complete; when the two are copies of the same book, the Clean job that runs after Link retires the worse one as
  it does today, and with part 3 that removes only its own file. When they are not the same book (two languages of
  one title), both stay.
- **Partial copy:** a copy is now written to a temporary name in the same folder (`.Book.epub.fetchshelf-partial`)
  and renamed into place when complete, so an interrupted copy never sits at the real name. A leftover temporary
  file is overwritten on the next attempt.

Size is the identity test on purpose: it works the same for hardlinks, copies and every file system, including
Windows. Two different files of one name and the exact same size are not worth an inode comparison that only works
on some platforms.

`LinkOutcomeDescription` and the Link job's log line mention files placed under another name:
`Linked Book into /library/Author/Book (1 file placed as "Book (2).epub": a different file of that name was already there)`.

### 3. Never delete a file another record lists

`LibraryFileRemover`:

- `IsFolderSharedAsync` becomes `FolderUseByOthersAsync(dbContext, libraryPath, torrentId)`, returning a
  `FolderUse`: whether another linked torrent uses the folder (which keeps the extras, as before), and the set of
  relative paths those torrents list in their `LinkedFilesJson` (case-insensitive).
- `Remove` takes that set as an optional parameter and skips every path in it. The Clean job's log line says how
  many files it kept that way.
- Callers: `Cleaner` (retire and unlink) and `LinkRunner` (relink). Both already call `IsFolderSharedAsync` right
  before `Remove`.

This also protects libraries where case 2 already happened: the duplicate record still lists the keeper's file,
but removing it leaves the file alone.

## Existing libraries

Torrents linked before this change are not re-placed by the Link job (it only places torrents that aren't linked
yet). Damage that is already there stays until the book is relinked:

- A torrent that lost files to case 1: **Relink** on its page removes its recorded files and places it again with
  the new rule, so all its files appear, with prefixed names where they clash.
- Duplicate records sharing a file (case 2): part 3 keeps the file safe from now on.

The planned Library check (`plans/25-Library-check.md`) is where finding such books belongs. Its "files missing"
finding covers part of it; it should also report *one file listed by two records* and *a torrent whose chosen
files outnumber its linked files*. That is added to that plan, not built here.

## Tests

- `LinkFilePlacerTests`: no clash keeps today's paths; `Part 1/01.mp3` + `Part 2/01.mp3` get prefixed names; a
  clash only through case is prefixed; the torrent's top folder is not part of the prefix; `Disc N` folders still
  become folders, and a clash inside one disc folder is prefixed within it; files without a clash in a torrent that
  has one keep their plain names.
- `FileLinkerTests`: an existing destination of the same size is `AlreadyExists`; of a different size, the file is
  placed as `name (2).ext` and the outcome says so; `(2)` taken gives `(3)`; an existing symlink to the source is
  `AlreadyExists`, to something else is not; a copy goes through a temporary name and no partial file remains at
  the real name when the copy throws.
- `LibraryFileRemoverTests`: a file another linked record in the same folder lists is kept, case-insensitively; a
  file only this record lists is removed; the extras rule is unchanged.
- `LinkRunnerTests`: a torrent with `Part 1/01.mp3` and `Part 2/01.mp3` links both and records both; a second
  torrent with a different `Book.epub` in the same folder is linked as `Book (2).epub`.
- `CleanerTests`: retiring the worse of two copies that share a file name keeps the kept copy's file.

## Documentation

`docs/user-guide.md`, the folder layout section:
- "Other folder structure is flattened" gains: when two files would get the same name, their sub-folder names are
  put in front of the file name (`Part 1 - 01.mp3`).
- When a different file of the same name is already in the folder (another copy or edition of the book), the new
  one is placed as `name (2).ext`.
- Unlink, Clean and Relink never remove a file another book in the same folder uses.

## Release notes

Under **Fixed**: files with the same name in different sub-folders of a torrent were linked only once; a file of
another torrent with the same name was taken as already linked and could be deleted when that torrent was cleaned.
Under **Upgrading**: nothing to do. Books linked before keep their layout; **Relink** on a torrent's page places one
again with the new rule. Written at release time.

## Out of scope

- Torrents that hold several books in sub-folders (packs): they are still linked as one book, with prefixed file
  names where names clash.
