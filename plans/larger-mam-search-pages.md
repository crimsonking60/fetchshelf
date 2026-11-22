# Larger MAM search pages — implementation plan

Not an issue yet: written 2026-11-22 from a conversation about MAM's API documentation.

## Context

Every MAM search Fetchshelf makes asks for 100 results (`MamSearchQuery.PerPage`, default 100), and a job that wants
more reads page after page, one request each, `SearchPagePoliteness` (400 ms) apart. MAM's documentation for
`/tor/js/loadSearchJSONbasic.php` says `perpage` takes 5 to 1000.

A test on 2026-11-22 (newest English freeleech audiobooks and ebooks, `perpage: 1000`, no description) got 1000
distinct torrents of the 1,005 found, in one request, in 0.4 s and about 1 MB: no slower than a typical 100-result
search on live (0.5-1.7 s in the MAM API page's records).

### Who reads more than one page

| Caller | Pages today | Effect of 1000 per request |
|---|---|---|
| `AutograbberRunner` | up to `max_pages` (50 for `bookmarks`, `freeleech`, `mine`; 1 otherwise) | A whole bookmark or freeleech list in a tenth of the requests: 5,000 results in 5 requests instead of 50. |
| `MamBookmarksSource` (`[[bookmarks_list]]`) | until the bookmarks run out | The same. |
| `FollowRunner` | up to 100 pages on a full read (`ReadAll`), usually one page afterwards (reads back to the last search) | A full read of a large series or author in fewer requests; the incremental read is one page either way. |
| `ListImporter` | per book, until both formats are settled; rarely more than one | None to speak of. |
| `UpgradeRunner`, cover lookups, the Download job's checks, lookups by id or hash | one | None. |
| Search page | one per page the user asks for | Not changed by this plan; see step 4. |

So the gain is for configurations that read long lists: large bookmark lists, `freeleech` or `mine` autograbs, and
follows of prolific series or authors. Live today reads three bookmarks a day and gains nothing measurable; this is
about fewer requests for larger installations, in keeping with the MAM API page's purpose, not about the 429s seen on
2026-11-21 (those were `.torrent` downloads, fixed in #173).

### What has to stay the same

- **`max_pages` keeps its meaning.** `docs/configuration.md` says "A page is 100 results (fixed)" and "`max_pages` × 100
  is the most results one run will read". Configurations in use rely on that window (a `new` autograb with one page
  sees the newest 100). The setting stays, documented as a number of hundreds of results; only the number of requests
  needed to read them goes down.
- **Stopping early stays cheap.** An autograb stops when it reaches its selection budget, Follow stops when it reaches
  torrents its last search saw, and every pager stops at a short page. A bigger page means more results read before
  such a stop, but never another request.

## Goals

1. One request per 1000 results for the callers above that read long lists, with `max_pages` and every other setting
   meaning what it means today.
2. No change for callers that read one page, and none on the Search page unless step 4 is done.
3. The number of requests a run made is visible as before (MAM API page, debug logs).

## Non-goals

- Changing `max_pages` into a count of 1000-result pages, or adding a setting for the page size.
- Genre filtering (MAM's search endpoint has none; see `plans/genre-support.md`).
- Sortable columns on the Search page (considered and dropped).

## Steps

### 0. Measure with descriptions

Every search asks for descriptions (`Description = true` in `MamClient.SearchAsync`), and the test above didn't. One
call with `perpage: 1000` and `description` set, recorded with its time and size, settles whether a page of 1000 with
descriptions is still reasonable (expected: a few MB, a second or two). If it is not, the steps below use 500 instead;
nothing else changes.

### 1. The client: one constant for the largest page

- `MamSearchQuery.MaxPerPage = 1000`, MAM's documented limit, next to the default `PerPage = 100`.
- `SearchResponseParser` and `MamSearchResult` are unchanged; `perpage` in the answer is already read.

### 2. A shared pager: results, not pages

A helper in `Fetchshelf.Data.AutoDownloader` (or `MamClient` itself) that reads up to N results of one query:

- Requests `perpage = min(1000, N - read so far)` from offset `read so far`, so `max_pages = 1` still makes one request
  for 100 results and `max_pages = 50` makes five of 1000.
- Hands each page's records to the caller as they come, so a caller can stop part-way through a page (the autograb's
  budget, Follow's read-back point) and the helper stops requesting.
- Stops at a short page, at MAM's `found`/`total` (the larger, as `FollowRunner` does now), or when a page brings only
  torrents already seen (the Follow guard against MAM not moving on).
- Keeps the 400 ms between requests that `SearchPagePoliteness` and the runners' `PolitenessDelay` give today.

### 3. The callers

- **`AutograbberRunner`:** reads up to `EffectiveMaxPages × 100` results through the pager. The per-record loop, the
  budget stop and `mark_removed` (the MAM ids returned) stay as they are.
- **`MamBookmarksSource`:** reads all bookmarks through the pager.
- **`FollowRunner.SearchPagesAsync`:** 1000 per request on a full read; 100 on an incremental read, where the first page
  nearly always reaches the read-back point and a larger one would only fetch more to throw away. `MaxPages` (100
  pages of 100) becomes a result limit of 10,000.
- **`ListImporter`, `UpgradeRunner`, lookups:** unchanged.

### 4. Optional: a Per page choice on the Search page

The Search page keeps 100 results a page by default; a page of 1000 rendered rows (badges, buttons, details) is several
MB of HTML and slow on a phone, and the page's Show, Hide downloaded and *Not snatched yet* filters only act on the
rows of the current page. A **Per page** choice (100, 250, 500, 1000), remembered by the browser as on the Torrents and
Events pages (`PageSizeCookie`), lets a user working through a long, heavily filtered list ask for more. Page numbers
and **Prev**/**Next** follow the chosen size; the address carries it, so a shared search keeps it.

Left out unless asked for when the plan is picked up.

### 5. Documentation

- `docs/configuration.md`, `max_pages`: "A page is 100 results" becomes "`max_pages` counts hundreds of results:
  `max_pages = 50` reads up to 5,000. Fetchshelf asks MAM for up to 1,000 at a time, so that is 5 requests."
- `docs/user-guide.md`, MAM API section: a line that long lists are read 1,000 results per request.
- With step 4: the Search page section's "results come 100 to a page".
- `RELEASES.md` at release time.

## Tests

- Pager: `max_pages = 1` makes one request with `perpage` 100; `max_pages = 50` against 2,345 results makes three
  requests (1000, 1000, 345 returned) and stops at the short page; a stop requested part-way through a page makes no
  further request; a page of only already-seen torrents stops it.
- `AutograbberRunnerTests`: the budget stop and `mark_removed` behave as before with 1000-result pages; the number of
  search requests for a 50-page bookmarks block.
- `FollowRunnerTests`: a full read uses 1000 per request, an incremental read 100, and the read-back stop still holds.
- `MamBookmarksSource`: 2,500 bookmarks in three requests.
- With step 4: the Search page's Per page choice, its cookie and the paging links.

## Open questions

1. **500 or 1000?** Decided by step 0's measurement.
2. **Follow's incremental read:** 100 per request as proposed, or 1000 for simplicity at the cost of more data per run?
3. **Step 4:** wanted now, later, or not at all?
