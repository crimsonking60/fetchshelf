# Hardcover lists: match by ISBN/ASIN, back off on books MAM doesn't have, search again after a lost selection — implementation plan

## Context

`HardcoverListImporter` searches MAM once per book, per format still missing, per `grab` block:

- **Matching is on title and author only.** The search is `ListMamQueryBuilder.Build(title, authors)`, first page
  only (`pageOffset: 0`); `ListCandidateRanker.RankAndFilter` ranks the results and `ListLibraryMatcher.FindMatch`
  decides whether the best is the same book. Hardcover knows each edition's ISBN and ASIN, and MAM results often
  carry one (`TorrentMetadata.Isbn` / `Asin`, from the `isbn` field, where `ASIN:` marks an ASIN). An identifier
  match is certain where a fuzzy title match is not, for example titles with a subtitle, or translated titles.
- **A book MAM doesn't have is searched daily forever.** `SearchAgainAfter` is one day. A Want to Read shelf of 300
  books, mostly not on MAM, costs about 300 searches per block per day, for years.
- **A selection that goes away is never replaced.** When a side is `Selected` (`ListItemSideStatus.IsResolved`), the
  book is never searched again for that format. If the selection is then removed — a stale link, MAM no longer
  having the torrent, a duplicate, or removed by hand on the **Selected** page — the list still says *selected* and
  nothing brings it back. Nothing in the code resets the side; the review asked for this to be checked, and it is so.

## Change

### 1. Identifiers from Hardcover

- `HardcoverClient`'s `BookFields` adds the editions' identifiers:
  `editions(limit: 20) { isbn_10 isbn_13 asin }`. **Check first** against Hardcover's GraphQL schema that the field
  and argument names are these, and how large the answer gets for a list of 300 books; if `editions` is too costly,
  `default_audio_edition { isbn_13 asin }` and `default_physical_edition { isbn_13 isbn_10 }` are the fallback.
- `HardcoverBook.Identifiers`: the distinct ISBN-13s (ISBN-10s converted to 13), and ASINs, upper-cased.
- `ListItemEntity.IdentifiersJson` (migration), filled on every sync like the title and authors.

### 2. Identifier first, then the fuzzy match

In `Best`, before ranking: a candidate whose ISBN (normalized to 13 digits, hyphens removed) or ASIN is among the
item's identifiers wins for its side outright, still subject to the side, the `grab` block's filter and cost and
the format preference. Only when none matches does the fuzzy ranking run, as today.

`ListCandidateRanker` stays pure and gets the identifier check as its first rule, so it is tested in Core.

### 3. A second page only when it can help

When the first page is full (MAM's 100 results) and neither an identifier nor the fuzzy match found the book, the
next page is searched, once. A first page with fewer results means there is no next page. This costs a second search
only for very common title words.

### 4. Back-off for books MAM doesn't have

- `ListItemEntity.MissingSince` (migration): set when a search finds nothing for any needed format (every needed
  side is *not found*); cleared when anything is found or the book is marked done.
- The wait before searching again grows with it: one day while the book has been missing for less than a week,
  then seven days. Constants in `ListSearchSchedule` (Core, pure): `NextSearch(lastSearched, missingSince, now)`.
- A book newly added to the list is searched on the next run, as now. **Search now** on a book's row on the Lists page
  clears `LastSearchedAt`, so a book can be retried by hand.

### 5. A lost selection is searched for again

During the list sync (before searching), for each item side that is `Selected`: when its MAM id has neither a
selection still waiting or started (`SelectedTorrents`, `RemovedAt` null) nor a torrent record, the side goes back
to unresolved (status `null`, MAM id kept as the last one seen) and the book is searched on this run. The Lists page
shows it as waiting again.

`Existing` sides are rechecked the same way against the library (the book's copy unlinked or retired), using the same
`TryMatchLibraryAsync` that set them.

## Tests

- `ListCandidateRankerTests`: an ISBN-13 match beats a better title match; ISBN-10 vs 13; ASIN; no identifier falls
  back to today's ranking.
- `ListSearchScheduleTests`: next search at one day, then seven; `MissingSince` cleared on a find.
- `HardcoverListImporterTests`:
  - a full first page without a match searches page 2 once; a short page doesn't;
  - a `Selected` side whose selection was removed is searched again and can be selected again;
  - a `Selected` side whose torrent is in the library stays selected;
  - an `Existing` side whose copy was unlinked is searched again.
- `HardcoverClientTests`: identifiers are read from a recorded answer.

## Documentation

- `docs/configuration.md`, `[[hardcover_list]]` "What each run does": identifiers first; the back-off ("daily for a
  week, then weekly"); that a lost selection is searched for again.
- `docs/user-guide.md`, Lists page: **Search now**.

## Release notes

- Added: Hardcover lists match books by ISBN or ASIN when the MAM upload has one, before comparing titles.
- Changed: a book MAM doesn't have is searched daily for a week, then weekly.
- Fixed: when a list's selection is removed from the queue, the book is searched for again instead of staying
  *selected*.
- Upgrading: nothing to do. Books that were left *selected* by a removed selection are searched again on the first
  run.

## Out of scope

- Searching MAM by ISBN directly: MAM's search fields (`MamSearchField`) don't include it.
- The same changes for `[[file_list]]`: it shares the importer once `plans/file-based-lists.md` is built, and gets
  identifiers from an ISBN column in the file.
