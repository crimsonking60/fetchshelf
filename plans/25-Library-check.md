# Library check: find library folders and records that don't match — implementation plan (#25)

## Context

Fetchshelf's records and the library folders on disk can drift apart, and nothing shows it. The clearest case
is going back to a database backup (see "Going back to an older version" in the user guide): the Link job
re-finds the torrents qBittorrent still has, but three things it can't put right:

1. **Folders no book points at.** A book whose folder name comes out differently when it is re-recorded
   (metadata edited by hand after the backup, or changed on MAM since) is linked into a new folder, and the
   old one stays behind. A book whose torrent was removed from qBittorrent after the backup is never found
   again, and its folder sits in the library unknown. Audiobookshelf then shows duplicates, and Clean and
   Unlink never touch those folders.
2. **Books whose folder is gone.** A book cleaned or unlinked after the backup is still recorded as linked,
   but its files are gone. The **Torrents** page, the OPDS catalog and Audiobookshelf matching all trust the
   record; an OPDS download of it gives 404.
3. **Books with files missing.** The folder exists, but some of the linked files don't.

The same drift happens without a restore: files deleted or moved by hand, a library folder restored from a
file backup, a crash during linking, a `library_dir` changed in the configuration.

The idea: a **Library check** that compares every `library_dir` with the records and lists what doesn't match,
with an action for each finding.

## What it finds

The check walks each configured `library_dir` and the linked torrents (`LibraryPath` not null), and reports:

| Finding | Means | Actions |
|---|---|---|
| **Unknown folder** | A folder holding files that is not a book's `LibraryPath`, nor inside one. | **Delete folder**, or nothing |
| **Folder missing** | A linked book whose `LibraryPath` doesn't exist. | **Relink** (when qBittorrent still has the torrent), **Remove record** |
| **Files missing** | A linked book whose folder exists, but some files from `LinkedFilesJson` don't. | **Relink**, **Remove record** |

Details:

- **Unknown folders are reported at the deepest level that holds files**, so `Author/Series/Title` shows as
  that one folder, not three. Folders inside a known book's folder (multi-disc books, `Disc 1`, …) belong to it.
  Folders that only hold Fetchshelf's own side files (`metadata.json`, `metadata.opf`, a cover) still count:
  they are what a failed or half-finished link leaves behind.
- **Only inside a `library_dir`.** The check never looks above or outside them, and doesn't follow symbolic
  links to folders.
- **Each finding shows enough to decide:** the folder, its size and number of files, the newest file's date,
  and for a record, the book's title, author and torrent page link. An unknown folder that looks like a known
  book (same normalized title and author as a linked book, via `TitleNormalizer`) says so: *probably an older
  folder of* that book. That is the common case after a restore, and the one that is safe to delete.
- **Relink** and **Remove record** are the existing torrent-page actions, so they behave as they do there.

## Design

- **A job, `Library check`,** registered in `IJobRegistry` with interval 0 (Run now only). Walking a large
  library on a NAS can take minutes, so it doesn't run on page load. It needs no MAM access, so no
  `MamAccessGate`.
- **`LibraryChecker`** in `Fetchshelf.Data/Organizer/` does the walk and returns the findings. The pure part,
  turning a list of folders with their files plus the known books into findings, goes in a Core type,
  `LibraryCheckComparer` in `Fetchshelf.Core/Organizer/`, with unit tests and no file system.
- **Results are kept in memory** (a singleton holding the last run's findings and when it ran), not in the
  database. They are a snapshot to act on, and are stale as soon as anything links. A restart forgets them;
  running the check again is cheap enough.
- **A page, `Pages/LibraryCheck/`,** linked from the **Jobs** page's entry for the job and from the menu next
  to **Errors**. It shows the findings grouped by kind and by library, with checkboxes and bulk actions like
  the **Torrents** page. It is added to the pages table in the user guide.
- **Deleting is checked again at click time.** The page posts a finding's id, never a path. Before deleting,
  the folder must still be inside a `library_dir` (by full path, after resolving `..`), must not be a
  `library_dir` itself, and no book may point at it or at a folder inside it now: the Link job may have used it
  since the check ran. A deleted folder's empty parents (an author folder left with nothing in it) are removed
  too, up to the `library_dir`. Each deletion is logged and recorded as an event.
- **Audiobookshelf:** deleting a folder doesn't delete its Audiobookshelf item, since no book points at it.
  Audiobookshelf marks the item missing on its next scan. Removing a record of a matched book deletes its
  Audiobookshelf item as Unlink already does.

## Scale

At 10,000 books the walk visits about 30,000 folders. The comparison loads `LibraryPath` and `LinkedFilesJson`
of the linked torrents once, into a set and a dictionary, so it stays linear. File sizes and dates come from
the same directory enumeration (`EnumerateFileSystemInfos`), not one call per file. A scale test like the
other 10k tests (`/data/tmp`) should keep a check of the 10k library under a minute on a hard disk.

## Open questions

1. **Bring an unknown folder back instead of deleting it.** For a book whose torrent is gone from
   qBittorrent, the folder is a complete book Fetchshelf could know again. Two ways:
   - a. **Write the MAM id into `metadata.json`** (it is already in `metadata.opf`), so a later check can
     offer **Download again**: select the torrent by its MAM id; the Download job adds it to qBittorrent, the
     Link job re-records it and finds its files already in the folder. MAM doesn't charge a second wedge for a
     torrent already snatched, but the download counts again against the ratio, which needs checking.
     Audiobookshelf must ignore the extra field in `metadata.json`.
   - b. Leave it out of this plan, and only delete.

   *Proposed:* b now, a as a follow-up once the `metadata.json` change is checked against Audiobookshelf.
2. **Folders the user keeps on purpose,** such as books added to a library folder by hand. They show up as
   unknown on every check. An ignore list (a setting, or a button that remembers the folder in the database)
   would quiet them. *Proposed:* not in the first version; the user guide says to keep such books outside the
   `library_dir`s.
3. **Menu entry or Jobs page only?** A page that is empty most of the time doesn't need a menu entry.
   *Proposed:* reachable from the job on the **Jobs** page, and from the menu only while the last check found
   something.
4. **Check while Link runs?** A Link run during the walk can make a folder look unknown for a moment.
   *Proposed:* the check and the Link job share a lock, like `MamAccessGate` but for the library, so they never
   run at the same time; the click-time recheck covers what happens after the check.

## Steps

1. `LibraryCheckComparer` in Core, with unit tests for each finding, nested folders, side-file-only folders
   and the *probably an older folder of* match.
2. `LibraryChecker` and the job: walk, compare, keep the results; tests against a temporary library folder.
3. The page and its actions: delete with the click-time checks, relink and remove record through the
   existing runners; Web tests for each action and for a deletion refused because a book now points at the
   folder.
4. User guide: the page in the pages table, a "Checking the library" section, and a step in "Going back to an
   older version" telling to run the check after the Link job has caught up. Configuration reference only if
   question 2 adds a setting.
5. The 10k scale test.

## Answers (2026-11-18)

1. Only delete for now; bringing a folder back is a follow-up.
2. No ignore list in this version; the user guide says to keep hand-added books outside the `library_dir`s.
3. Reachable from its job on the **Jobs** page, and from the menu only while the last check found something.
4. The check takes the library write gate (`LibraryWriteGate`, PR #79), which the Link and Clean jobs and bulk actions
   already share, so no new lock.
5. (New, for the series layout of PR #92.) In a series library, files in a series folder that no book lists are
   reported as **Unknown file**.

## As built

- **Core:** `LibraryCheckComparer` takes the walked folders (each with its direct files), the libraries and the linked
  books, and returns the findings; reading an unknown folder's `metadata.json` is passed in, and asked only for unknown
  folders. An unknown folder is the outermost folder with files that is no book's folder, inside none and above none.
  Files in a library's own folder, or in a folder above book folders (an author folder), are **Unknown file** too, so
  such a folder is never offered for deletion with books below it.
- **Data:** `LibraryChecker` walks each `library_dir` with one `EnumerateFileSystemInfos` per folder, skipping names
  starting with `.` or `@` (a NAS's `@eaDir`) and folder links, and also the folder of each book outside every
  `library_dir`. `LibraryCheckResults` (singleton) keeps the findings with ids. `LibraryCheckDeleter` re-checks at click
  time, deletes, removes empty parents (`LibraryFileRemover.RemoveEmptyParents`, now public) and appends a
  `DeletedFromLibrary` event (a new `EventKind`, at the end).
- **Job:** `Library check` (reserved as a name), registered with the organizer jobs and never scheduled. Its result
  counts the findings by kind.
- **Page:** `Pages/LibraryCheck/`: **Run the check** (which waits up to 2 seconds, so a small library's check comes back
  with its findings), unknown folders and files with **Delete selected**, books with **Relink selected** and **Remove
  record** (as the torrent page's, which doesn't touch Audiobookshelf). Relink runs from the page, under the write gate,
  one book after the other: a book relinked leaves the list, one that couldn't be stays with the reason noted on it.
  First queued as the Torrents page's bulk Relink, which dropped the findings at once and reported failures only in the
  log; changed after trying it on the dev instance. While a check runs, the page shows the previous findings with the
  actions off, and reloads itself when it ends; a post for findings a newer check replaced does nothing and says so.
- **Relative paths** on record (left from an older configuration) and in the configuration are resolved against the
  working folder with `Path.GetFullPath`, as Fetchshelf's file operations resolve them. The Jobs page links the job to the page; the menu
  shows it with a count while there are findings.
- **Scale:** the 10k seeded library laid out on disk (`/data/tmp`, a hard disk) was checked in 0.6 s, with the files
  just written, so from the disk cache.
