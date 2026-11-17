# MAM bookmarks as a list on the Lists page — implementation plan

## Context

A `type = "bookmarks"` autograb downloads what is bookmarked on MAM, through the normal selection rules. What
happened to each bookmark is spread over the log, the Selected page and the Duplicates page: whether it was
selected, turned down as a duplicate, held back by its cost, or is waiting for budget. The Lists page already shows
that kind of per-book state for Hardcover lists (and file lists, `plans/file-based-lists.md`).

A bookmark is different from a list book in one way: it names a torrent, not a book. There is nothing to search
for; the question is only whether that torrent is wanted and affordable.

## Depends on

`plans/file-based-lists.md` step 1: the source-neutral `ListImporter` with `IListBookSource`, `ListBook` and
`ListDefinition`. This plan adds a third source.

## Change

### 1. A known torrent on a list book

- `ListBook.MamId` (optional). When set, the importer doesn't search MAM for the book: it looks that torrent up by
  id (`MamSearchQuery { Id = … , ReturnDownloadLink = true }`) and treats it as the only candidate, for its own
  format side. The other side is not wanted (`NotWanted` with no MAM id).
- The rest is unchanged: the library check, the `grab` blocks' filter and cost, selection, the item statuses.

### 2. The source

- `MamBookmarksSource : IListBookSource`: pages through `MamSearchTarget.Bookmarks` (newest bookmark first, every
  page, under the importer's gate) and returns one `ListBook` per torrent: `ExternalId` = the MAM id, title, authors,
  series, `MamId`, cover from the thumbnail if asked for.
- A torrent unbookmarked on MAM leaves the list (`RemovedAt`), as a book leaving a Hardcover list does.

### 3. Configuration

```toml
[[bookmarks_list]]
name = "MAM bookmarks"
search_interval = 60

[[bookmarks_list.grab]]
cost = "wedge"
```

- `name`, `category`, `grab`, `search_interval`, `dry_run` as for `[[hardcover_list]]`; `prefer_format` is
  refused (a bookmark is one format already). At most one `[[bookmarks_list]]`: there is one bookmark list.
- Job name `bookmarks_list` unless named; list id `mam:bookmarks`.
- An `[[autograb]]` with `type = "bookmarks"` and a `[[bookmarks_list]]` together are refused at load: both would
  select the same torrents. The configuration reference says which to choose: the list for per-bookmark state on
  the Lists page, the autograb for the shared filter's full options (uploader, seeders, `max_active_downloads`).

### 4. Lists page

The list shows like the others, with each row linking to the torrent's detail view on the Search page instead of a
Hardcover book page.

## Tests

- `ListImporterTests`: a book with `MamId` is looked up by id and not searched; its other side is not wanted; a
  torrent that is gone from MAM is recorded as not found.
- `MamBookmarksSourceTests`: paging, mapping, an empty bookmark list.
- Config tests: two bookmark lists; a bookmarks autograb together with a bookmarks list; `prefer_format` refused.
- `ListsPageTests`: bookmark rows link to the Search detail view.

## Documentation

- `docs/configuration.md`: `[[bookmarks_list]]`, and the choice against a bookmarks autograb.
- `docs/user-guide.md`, Lists page: the bookmarks list.

## Release notes

- Added: `[[bookmarks_list]]` shows MAM bookmarks on the Lists page with what happened to each, and downloads them
  through its `grab` blocks.
- Upgrading: nothing to do. A bookmarks autograb keeps working; replace it with a `[[bookmarks_list]]` to see each
  bookmark's state.

## Out of scope

- Removing a bookmark on MAM once its torrent is downloaded (see `plans/search-bookmark-and-more-like-this.md`).

## As built

- **No lookup by id.** The bookmarks search already returns every bookmarked torrent's full record, download link
  included. So `MamBookmarksSource` keeps those records, and the importer gets each one through
  `IListBookSource.KnownTorrent(externalId)` instead of `ListBook.MamId` and a `MamSearchQuery { Id }` per book. A run
  makes one MAM request per 100 bookmarks, and none per bookmark. A torrent no longer bookmarked or gone from MAM leaves
  the list, so the "gone from MAM is recorded as not found" case doesn't arise.
- **Looked at on every run.** Checking a known torrent needs no search, so it isn't held to the daily or weekly search
  schedule: a bookmark not yet selected or in the library is looked at on every run, and one that turns freeleech is
  taken on the next. The list's page shows no next search or **Search again** for bookmarks.
- **Its own format only.** A bookmark's item allows only its torrent's format (`AllowAudio` or `AllowEbook`), so
  the other format doesn't show and doesn't count as missing. A torrent that is neither, or can't be read, is left out.
- **A new side status, `NoGrabBlock`.** It is used for a bookmark that no grab block's filter takes: "On MAM, but none
  of the list's grab blocks' filters takes it". It isn't resolved, so a config change is picked up. It is stored as
  text, so it needs no migration.
- **Covers.** No covers are shown: thumbnails would load from MAM's CDN on the Lists page, and only the Search page's
  details and the Link job (`covers-from-mam.md`) may fetch covers.
- **The gate.** The source takes the MAM gate itself while it reads the bookmarks (as "List {name}"), and releases it
  before the importer takes it for the account read and the selections.
- **Errors page.** Errors go under a new `BookmarksList` error stage ("Bookmarks list", keyed by MAM id).
- **Run summary.** A bookmarks run says "N bookmarks, M looked at, K selected" rather than "searched on MAM".
- **Config.** "Only one" is checked before the names, so two unnamed bookmarks lists get that message. `bookmarks_list`
  is refused as another block's name. `5-everything.toml` has the block commented out, as it holds a bookmarks
  autograb.
- **Release notes** are left for the release PR.
