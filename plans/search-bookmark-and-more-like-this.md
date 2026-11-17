# Search page: "more like this" — implementation plan

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

### 2. Bookmark on MAM: not built

Checked 2026-11-17: MAM's API documentation lists, under "Items listed here are the only parts of site you are
authorized to automate", these endpoints and no others: the bonus points store (`/json/bonusBuy.php`), torrent
download (`/tor/download.php`), dynamic seedbox IP (`/json/dynamicSeedbox.php`), IP and ASN lookup
(`/json/jsonIp.php`), load user data (`/jsonLoad.php`), torrent search (`/tor/js/loadSearchJSONbasic.php`) and user
bonus history (`/json/userBonusHistory.php`). There is no bookmark endpoint, so bookmarking from Fetchshelf isn't
authorized, and this part is dropped. Reading bookmarks stays: the search endpoint searches them (type
`bookmarks`), and its answer's `bookmarked` field says whether a torrent is bookmarked.

## Tests

- `SearchQueryBuilderTests`: `MoreLike` for each field keeps languages and categories, resets the page, quotes
  names with spaces.
- Search page markup: the links are there for authors, narrators and series.

## Documentation

- `docs/user-guide.md`, Search page: names are links to more by the same author, narrator or series.

## Release notes

- Added: authors, narrators and series on the Search page link to a search for more of the same.
- Upgrading: nothing to do.

## Out of scope

- Bookmarking, and removing the bookmark after a bookmark autograb downloaded the torrent: MAM authorizes no
  bookmark endpoint (see section 2).
