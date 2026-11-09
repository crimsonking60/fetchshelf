# Library check: find library folders and records that don't match — implementation plan

Not yet an issue.

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
