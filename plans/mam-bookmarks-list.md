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
