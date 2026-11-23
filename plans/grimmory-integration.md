# Grimmory integration

Grimmory is a self-hosted library and reader for ebooks, comics and audiobooks (Java/Spring, a Booklore fork). Two
ways of feeding it Fetchshelf's books were weighed:

- **A. Library:** a Grimmory library whose path is a Fetchshelf `library_dir`.
- **B. Bookdrop:** Fetchshelf puts each book into Grimmory's bookdrop folder (or uploads it), and Grimmory imports it.

Findings below are from the Grimmory source (`../grimmory`, at `cc2f82f3`); paths are under
`backend/src/main/java/org/booklore/`.

## Recommendation

**A, the library, with nothing in Fetchshelf to begin with but documentation (step 1).** Fetchshelf keeps owning the
files and the folder tree, so Library check, Relink, Clean, upgrades and OPDS keep working; Grimmory reads the same
hardlinks without taking extra disk. B makes Grimmory own a separate copy of every book, out of Fetchshelf's reach,
and needs a call per batch to import anything.

Step 2, a `[grimmory]` section that asks Grimmory to rescan, is worth it only for libraries Grimmory can't watch
(network shares); step 3, matching and metadata, only if step 1 shows Grimmory's own metadata falls short.

## What Grimmory does, by approach

### A. A library on a Fetchshelf `library_dir`

- **Libraries.** A library has one or more paths (`model/entity/LibraryEntity.java:31`), and per library a
  `watch` flag (default off; the UI's default too), an organization mode and a file naming pattern.
- **Finding files.**
  - A full scan when the library is created or a path added, and a daily one (`SYNC_LIBRARY_FILES`, 01:00).
  - With `watch` on, an inotify watcher (`service/monitoring/LibraryWatchService.java`) on create and delete
    events, which also sees renames into the tree. Hardlinks keep the source's mtime, so they pass its "file still
    being written" wait at once. inotify doesn't see changes another host makes on a network share.
  - `PUT /api/v1/libraries/{id}/refresh` rescans a whole library; there is no rescan of one folder.
- **Formats.** pdf, epub, cbz, cbr, cb7, mobi, azw3, azw, fb2, and m4b, m4a, mp3, opus. A folder of two or more
  audio files and no ebook is one audiobook; all-m4b folders with distinct titles are a book per file.
- **Organization mode.** `BOOK_PER_FILE` (the default) makes an EPUB and a PDF in one folder two books;
  `BOOK_PER_FOLDER` makes them one book in two formats, and is the only mode (with folder audiobooks) that uses a
  folder's `cover.jpg` when the file has no cover. **Fetchshelf's book layout wants `BOOK_PER_FOLDER`.**
- **Metadata.** Read from inside the files (an EPUB's own OPF, audio tags) and from Grimmory's providers. Fetchshelf's
  `metadata.opf` and `metadata.json` are not read. Grimmory's own sidecar (`<book>.metadata.json`,
  `<book>.cover.jpg`) is read only by a manual import endpoint, never by a scan.
- **Writing into the library: all off by default**, and each must stay off:
  - `saveToOriginalFile` per format: an EPUB or PDF is rewritten through a temporary file (the hardlink breaks, the
    torrent's copy is unharmed), but **audio tags are written into the file itself**
    (`service/metadata/writer/AudiobookMetadataWriter.java:112,210`), which changes the seeding torrent's data;
  - `moveFilesToLibraryPattern`: moves and renames files after a metadata edit, out of Fetchshelf's tree;
  - `convertCbrCb7ToCbz`, and sidecar writing;
  - for a folder audiobook, saving a cover deletes `cover.*`/`folder.*` and writes a new one.

  `DISK_TYPE=NETWORK` in Grimmory's environment turns off file, cover and sidecar writes and moves as a whole, which
  makes a safe setup independent of those settings. It doesn't stop **deleting a book in Grimmory, which deletes its
  files** (a folder audiobook's whole folder, `service/book/BookService.java:359-400`). For a hardlink that loses only
  the library copy, not the torrent; Library check then reports the folder missing, and Relink places it again.
- **Moves.** With the watcher, a file that vanishes and turns up elsewhere within about a minute is matched by a
  partial MD5 and keeps its book, with its reading progress and shelves. A rescan matches by path only: a moved file
  is deleted (progress, shelves, notes cascade away) and comes back as a new book. **A Relink that moves a book keeps
  its Grimmory progress only with `watch` on.**
- **To verify in step 1:** the first scan collapses audio files into a folder audiobook even with an EPUB beside them,
  while rescans don't (`service/library/LibraryFileHelper.java:194-218` against
  `service/library/LibraryProcessingService.java:89`). Fetchshelf links a torrent's best audio and best ebook into
  one folder, so such a book may be removed and added again on the first rescan.

### B. Bookdrop

- One folder (`APP_BOOKDROP_FOLDER`, `/bookdrop`), watched at its top level; a new subfolder is read once, files
  added to it later only by a rescan.
- **One entry per file**, folders ignored: an audiobook in 40 mp3 files is 40 entries.
- Every file waits for review. Importing is `POST /api/v1/bookdrop/imports/finalize` (a JWT, bookdrop permission),
  which an outside caller can make with `selectAll`.
- An imported file is **copied** into the library under Grimmory's naming pattern (`Files.copy`, a new inode, never a
  hardlink; `service/bookdrop/BookDropService.java:423`), then the dropped file is deleted. A target that exists
  fails the import.
- The upload endpoints (`POST /api/v1/files/upload`, `.../upload/bookdrop`) move an uploaded file the same way, with
  a 100 MB default limit.

Against Fetchshelf's model that means: every book twice on disk; a folder tree Fetchshelf doesn't know, so no Library
check, Relink, Clean of a replaced copy or upgrade swap reaches Grimmory's copy; audiobooks in many files broken into
pieces; and Fetchshelf has to call the API to get anything in. Its one strength, review before import, isn't wanted
for books Fetchshelf already chose. **Not recommended.**

## Steps

### 1. Documentation: using Grimmory on a Fetchshelf library

A user guide section, next to Audiobookshelf and Komga:

- Mount the `library_dir` into Grimmory (read-only where Grimmory allows it), not the downloads.
- One Grimmory library per Fetchshelf book-layout library, `BOOK_PER_FOLDER`, `watch` on for a local disk.
- `DISK_TYPE=NETWORK`, and why: Grimmory must never write into the files, which are the seeding torrents' own.
- Leave `saveToOriginalFile`, `moveFilesToLibraryPattern`, CBR conversion and sidecars off.
- Remove books with Fetchshelf's Unlink, not Grimmory's delete; what happens when Grimmory's delete is used.
- Reading progress survives a Relink with `watch` on, not across a rescan.
- Series-layout libraries (Komga) are not for Grimmory: there a series folder holds every book.

Before writing it, set it up against a scratch Grimmory (`dev.docker-compose.yml`) on a demo library from the fake
servers, and check: one book per folder; `cover.jpg` used; an EPUB with an m4b, and with mp3s, in one folder; a Relink
that moves a book keeps it; Fetchshelf's own files (`metadata.opf`, `metadata.json`) don't show as books.

### 2. Optional: `[grimmory]`, to rescan after Link

Only for libraries Grimmory can't watch. A `[grimmory]` section with `url`, `username`, `password` (a secret, as the
`[[notify]]` URLs: never logged or shown) and a `path_mapping` like `[audiobookshelf]`'s.

- Grimmory has no API keys: log in (`POST /api/v1/auth/login`), keep the access token (2 hours) and its refresh token
  (30 days), log in again when both have run out. The user needs the library-manage permission.
- After a Link, Clean, Relink or bulk run that changed folders, find the Grimmory libraries whose paths hold them
  (`GET /api/v1/libraries`) and `PUT /api/v1/libraries/{id}/refresh` each, at most once per run.
- Say in the documentation that a rescan loses reading progress of moved books, and prefer `watch` where possible.
- A `Fetchshelf.Clients.Grimmory` client, its options record, a Config page row, the Health/Account check of the
  login, `5-everything.toml`.

### 3. Optional: matching and metadata

Only if step 1 shows Grimmory's metadata falls short for Fetchshelf's books (MAM's series and narrators, hand
edits). Match each linked book to its Grimmory book by path, as the Audiobookshelf matcher does, store the id, link to
it from the torrent pages, and push metadata with `PUT /api/v1/books/{id}/metadata` after Link and Refresh. Whether a
Grimmory book's file paths can be read from the API (`GET /api/v1/libraries/{id}/book`) is to be checked first.
Writing Grimmory's sidecar files instead is not proposed: a scan doesn't read them, and they would be more files in
the folders that Relink, Unlink and Clean must know.

## Not planned

- Bookdrop and upload (B), for the reasons above.
- Anything that has Grimmory write into or move library files.
