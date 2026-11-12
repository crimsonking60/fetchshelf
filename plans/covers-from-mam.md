# Covers from MAM for books without one — implementation plan

## Context

A torrent's cover is whatever image the upload contains. Many audiobook uploads have none, or name it
`folder.jpg`, `cover.png` or after the book. Audiobookshelf then shows a blank cover until it finds one itself, and
OPDS readers and file browsers show nothing.

MAM has a cover for most uploads. The Search page's detail view already shows it: the search answer's `thumbnail`
(`MamSearchQuery.ReturnThumbnail`), or the poster address built by `MamHitDetails.CoverUrl` from the torrent id and
poster type, fetched with the session by `MamClient.DownloadImageAsync`, which only fetches MAM's own addresses
(`IsMamAddress`).

`cover.jpg` in a book's folder is already treated as Fetchshelf's own extra: `LibraryFileRemover` removes it with
`metadata.json` and `metadata.opf` when the folder is no longer shared.

## Change

### 1. When a cover is written

After the Link job places a book's files, and only when:

- the library sets `write_cover = true` (new, per `[[library]]`, default `false`);
- no image among the linked files is a likely cover: a `.jpg`, `.jpeg`, `.png` or `.webp` file, any name;
- `cover.jpg` doesn't exist in the folder yet (an existing one, from the user or Audiobookshelf, is never
  replaced).

### 2. Fetching it

- `MamHitDetails.ThumbnailUrl`/`CoverUrl` move from `Fetchshelf.Web/Pages/Search/` to `Fetchshelf.Clients.Mam`, so
  the Data project can build the same address; the detail page uses them from there.
- `MamCoverFetcher` (Data): looks the torrent up with `ReturnThumbnail` (one search by id, under `MamAccessGate` like
  `MamMetadataLookup`), builds the address as the detail page does, and fetches it with `DownloadImageAsync`.
- At most 50 covers per Link run, so a large first import doesn't send thousands of requests; the rest are fetched
  in later runs. Torrents still without a cover are found by `CoverFetchedAt` being null (new column, migration) and
  `write_cover` on their library.
- Written as `cover.jpg` through a temporary name. A PNG or WebP answer is written with its own extension
  (`cover.png`), and `LibraryFileRemover`'s extras list gains `cover.png` and `cover.webp`.
- A failure (no poster, HTTP error) records `CoverFetchedAt` anyway and logs at Information; covers are a nicety
  and don't go on the Errors page. **Refresh** on a torrent's page clears `CoverFetchedAt`, so it is tried again.

### 3. Audiobookshelf

When the book is already matched (`AbsId`), nothing more: Audiobookshelf picks up `cover.jpg` on its next scan.

## Tests

- `CoverNeededTests` (Core, pure): image files present or not, existing `cover.*`, the library setting.
- `LinkRunnerTests`: with `write_cover`, a book without an image gets `cover.jpg` from a fake MAM; with an image, no
  request; the per-run cap; a failure doesn't fail the link.
- `LibraryFileRemoverTests`: `cover.png` is removed with the other extras.

## Documentation

- `docs/configuration.md`, `[[library]]`: `write_cover`.
- `docs/user-guide.md`: in the library section, where covers come from.

## Release notes

- Added: `write_cover = true` on a library writes MAM's cover image into book folders that have none.
- Upgrading: nothing to do; off unless set.

## Out of scope

- Replacing low-resolution covers, or embedding covers into audio files.
