# Search page: paging, search fields, sort, search type and a category at download — implementation plan

## Context

The Search page (`Pages/Index.cshtml`, `IndexModel.OnGetResultsAsync`) sends MAM one fixed query:

```csharp
new MamSearchQuery
{
    Text = Q,
    SearchFields = [MamSearchField.Title, MamSearchField.Author],
    Target = MamSearchTarget.New,
    ReturnDownloadLink = true,
    LanguageIds = ...,
}
```

So a search reads the first 100 results, newest first (MAM's order for a text search is by relevance), matches
title and author only, and searches every torrent whatever it costs. A series name, a narrator or a word from
the description finds nothing unless it is also in the title. There is no way to see result 101.

`MamSearchQuery` already supports everything below except two sort orders and a results flag: the autograb
blocks use `PageOffset`, `SearchFields` (`search_in`), `SortOrder` (`sort_by`), `Kind` and `Target`
(`type`). This plan exposes them on the page.

A Download click calls `SelectionRunner.ProcessCandidateAsync(..., autograbCategory: null, ...)`, so the
qBittorrent category, and with it the library, comes from the `[[tag]]` rules only. The selection already takes a
category that overrides the rules, as an autograb's `category` does; the page just never passes one.

## Change

All new choices are GET parameters on the page, like `Q` and `Lang`, so a search can be bookmarked and the back
button works. Each defaults to today's behavior, so a plain search is unchanged.

### 1. Paging

- New parameter `PageNumber` (1-based, default 1; not `Page`, which Razor Pages keeps for itself, and the name
  the Torrents page uses). The query sets `PageOffset = (PageNumber - 1) * 100`.
- Below the results: "Results 101–200 of 1,234" from `MamSearchResult.Found`, and Previous / Next links that
  keep every other parameter. Next is left out on the last page; Previous on the first.
- The Show (media) filter and "Hide downloaded torrents" stay browser-side, so they filter within the page. The
  count line says so when a filter hides rows ("12 hidden by the filters above").
- Changing the search text, the fields, the sort or the type goes back to page 1.

### 2. Search fields

- New parameter `In`, repeated: `title`, `author`, `series`, `narrator`, `description`, `tags`, `filenames`.
  Default `title` and `author`.
- Shown as a row of checkboxes under the search box, collapsed into a "Search in: title, author" summary
  (`<details>`), so the toolbar stays one line by default.
- Mapped with the same parsing as the autograb `search_in` (`AutograbSearchOptions`), so both accept the same
  names. An unknown name is ignored, not an error: it comes from a URL.
- With no checkbox ticked the default is used, rather than searching nothing.

### 3. Sort

- New parameter `Sort`: `relevance` (default: MAM's own order, no `sortType` sent), `newest`, `oldest`,
  `seeders`, `snatched`, `size`, `title`.
- `MamSortOrder` gains `MostSeeders` (`seedersDesc`), `MostSnatched` (`snatchedDesc`), `LargestFirst`
  (`sizeDesc`) and `TitleAsc` (`titleAsc`). These four are also accepted by `sort_by` in `[[autograb]]` blocks
  (`high_seeders`, `high_snatches`, `largest_first`, `title`, matching the existing `low_seeders` and
  `low_snatches`), since the mapping is shared; `docs/configuration.md` lists them.
- `newest` needs one more value. `MamSortOrder.NewestFirst` sends no `sortType` at all (`MamClient.Search.cs`),
  which MAM sorts by date only when there is no search text; with text it sorts by relevance. So:
  - the page's `relevance` uses `NewestFirst` as today (no `sortType`);
  - the page's `newest` uses a new `NewestByDate`, sent as `dateDesc`;
  - `sort_by = "newest_first"` in an autograb is left as it is. An autograb with a `query` and no `sort_by` is
    therefore sorted by relevance, not date, as it always has been; `docs/configuration.md` says so, and a later
    change can decide whether it should send `dateDesc`.
- A dropdown next to the language one, submitting on change like it does.

### 4. Search type

- New parameter `Type`: `all` (default), `freeleech`, `free_or_vip`, `vip`, `bookmarks`, `not_snatched`.
  - `freeleech`, `free_or_vip` and `vip` set `Kind`.
  - `bookmarks` sets `Target = Bookmarks`, the same as the bookmarks autograb type.
  - `not_snatched` has no MAM search parameter. MAM's results carry `my_snatched`; `MamTorrentRecord` gains
    `IsSnatchedByMe` read from it, and the page drops those rows. Like the Show filter, this filters within the
    page, and the count line says how many were dropped. `my_snatched` is a required field of every search
    result in the reference client's model, so MAM sends it; a result without it, or with null, counts as not
    snatched.
- A dropdown in the toolbar. The heading names the type when it isn't *All torrents*: "Freeleech on MAM",
  "Matches for 'dune' (My bookmarks)".
- Every result row shows whether the account has snatched it (a small "snatched" label), from the same flag,
  whatever the type.

### 5. A category at download

- A dropdown beside the Download buttons at the top of the results: "Category: by tag rules" (default), then
  each `[[library]]` with a `category`, by library name ("Audiobooks (audiobooks)"), then each other category
  qBittorrent knows (from `QBittorrentPresenceCache`, when loaded).
- The chosen value is sent with every Download and "Download with wedge" click on the page (`hx-include`), and
  passed to `ProcessCandidateAsync` as `autograbCategory`. Tags still come from the `[[tag]]` rules.
- The choice is remembered in a cookie for the page, as people usually download into one library at a time.
- A value that is neither a library category nor known to qBittorrent is refused with a message, so a stale
  cookie or a hand-made POST can't create categories.
- The download feedback line names the category used.

## Tests

- `SearchQueryBuilder` (new, pure, in `Fetchshelf.Web/Pages/` or Core): builds the `MamSearchQuery` from the
  page parameters. Tests for the defaults (same query as today), each field, each sort, each type, the page
  offset, unknown names ignored, no fields ticked.
- `MamSortOrder` wire values for the five new orders (`NewestByDate` sends `dateDesc`, `NewestFirst` still sends
  nothing); `sort_by` accepts the four new names in config tests.
- `MamClient` search response tests: `my_snatched` read when present and false when missing.
- Search page tests (with the fake MAM handler the page tests use): the paging line and links for the first,
  a middle and the last page; `not_snatched` dropping rows; the category passed to the selection; an unknown
  category refused.

## Documentation

- `docs/user-guide.md`, "Finding and downloading books": search fields, sort, type, paging and the category
  dropdown; that the Show filter, "Hide downloaded torrents" and "not snatched" work within the page shown.
- `docs/configuration.md`: the four new `sort_by` values.

## Release notes

- Added: the Search page pages through all results, searches series, narrators, descriptions, tags and file
  names, sorts by seeders, snatches, size or title, limits a search to freeleech, VIP, bookmarks or books not
  snatched yet, and downloads into a chosen category.
- Added: `sort_by` accepts `high_seeders`, `high_snatches`, `largest_first` and `title`.
- Upgrading: nothing to do.

## Out of scope

- Bookmarking on MAM from a result.
- "More like this" links (same author, series or narrator).
- Media type and category filters sent to MAM instead of applied in the browser.
