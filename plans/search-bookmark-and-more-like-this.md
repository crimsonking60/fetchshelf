# Search page: bookmark on MAM, and "more like this" — implementation plan

## Context

Items 6 and 7 of the Search page proposal, left out of `plans/done/search-page.md`:

- **Bookmark on MAM.** Many people use MAM bookmarks as their wanted queue: an `[[autograb]]` with
  `type = "bookmarks"` downloads what is bookmarked, and the Search page can search bookmarks
  (`SearchQueryBuilder`, type `bookmarks`). Bookmarking itself still means opening MAM.
- **More like this.** A result shows its authors, and the detail view its series and narrators, as plain text.
  Searching for the same author, series or narrator means typing it.

Fetchshelf only reads from MAM today, apart from downloads. Bookmarking writes to the account, through an endpoint
Fetchshelf doesn't use yet.

## Change

### 1. More like this (no new MAM endpoint)

- In the result row and the detail view, each author, narrator and series name links to the Search page with a new
  search: the name as the text, the matching field only (`Author`, `Narrator` or `Series`, which `MamSearchField`
  has), the current language and category choices kept, page 1, in the default order (MAM has no sort by series
  number).
- Built by `SearchQueryBuilder.MoreLike(field, name, currentChoices)` (pure), returning the route values, so the
  links are plain URLs (open in a new tab, bookmarkable).
- Names are quoted in the query (`"Brandon Sanderson"`) when they contain spaces, so MAM matches the whole name.
  **Check first** that MAM's search treats double quotes as a phrase in the author/series/narrator fields; if not,
  the name is sent as plain words.

### 2. Bookmark on MAM

**Check first:** MAM's API documentation for the bookmark endpoint (adding and removing a bookmark by torrent id),
its parameters and answers, and that the search answer's `bookmarked` field (in the search JSON, not read today)
says whether a torrent is bookmarked. If there is no documented endpoint, this part is not built.

Then:

- `SearchResponseParser` reads `bookmarked` into `MamTorrentRecord.IsBookmarked`.
- `MamClient.SetBookmarkAsync(mamId, bool bookmarked)`: the documented call, through the normal request path (pause,
  rate-limit handling, no retry).
- The Search page row and the detail view get a ☆/★ toggle (htmx, swaps the row's status cell). It takes the MAM gate
  as `Search page` with the short wait (`plans/mam-gate-on-pages.md`); busy → "MAM is busy …".
- The row's statuses show *Bookmarked*.
- The toggle is logged at Information ("Bookmarked MAM torrent 123456 from the Search page").

## Tests

- `SearchQueryBuilderTests`: `MoreLike` for each field keeps languages and categories, resets the page, quotes
  names with spaces.
- Search page markup: the links are there for authors, narrators and series.
- `SearchResponseParserTests`: `bookmarked` read, missing field → false.
- `MamClientTests`: the bookmark request as documented; no retry.
- `IndexPageTests`: the toggle calls MAM once and returns the row with the new state; busy gate → message, no call.

## Documentation

- `docs/user-guide.md`, Search page: names are links to more by the same author, narrator or series; the bookmark
  toggle, and that a bookmark autograb then picks the torrent up.

## Release notes

- Added: authors, narrators and series on the Search page link to a search for more of the same.
- Added: bookmark and unbookmark torrents on MAM from the Search page.
- Upgrading: nothing to do.

## Out of scope

- Removing the bookmark after a bookmark autograb downloaded the torrent (a setting on the autograb), which can
  follow once the endpoint is in the client.
