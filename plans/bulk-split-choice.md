# Bulk split choice for collections

Written 2026-11-19.

## Why

**Split into its books** on a torrent's page stores a choice on that torrent (`TorrentEntity.SplitCollection`), and
the choice overrides the library's `split_collections` from then on, also for bulk **Relink**. A library whose
collections were split one torrent at a time therefore can't be put back by changing the configuration: each torrent
has to be opened, unticked and relinked by hand. The live instance had 28 such torrents (27 in `comics`, one in
`ebooks`) when this came up, and they were put back with an SQL update.

The same is true the other way: splitting many collections that the library doesn't split means one page each.

## What

1. **A bulk action on the Torrents page: "Split collections".** It sits with the other bulk actions, with a dropdown
   like **Set category**'s:
   - **As the library does** — clears the torrent's own choice (`SplitCollection = null`).
   - **Split** — stores `true`, or `null` when the library splits anyway (as the torrent page does: a choice equal to
     the library's isn't stored).
   - **Don't split** — stores `false`, or `null` when the library doesn't split.

   The choice is stored and each torrent is then relinked, as **Refresh and relink** on its page does without the MAM
   refresh. It runs in the background through `BulkOperationQueue`/`BulkOperationWorker` like **Relink**, under the
   library write gate. Torrents on the audiobook side, and books of a collection, are passed over (a book's row acts for
   its collection, as **Relink** already does: the action applies once per collection).

2. **A filter to find them: "Split choice"** on the Torrents page, with *Set on the torrent* (any stored choice),
   *Split* and *Not split* (`CollectionBookCount` set or not). With the **Library** filter it finds "the collections of
   `comics` split one by one" without SQL, and **Select all** then covers them.

3. **The torrent page** says when its choice is its own and not the library's ("Chosen for this torrent; the library
   doesn't split"), so a stored override is visible where it is set.

## How

- `BulkAction.SetSplitChoice` with an argument (`library`, `split`, `noSplit`), carried like **Set category**'s
  `category`. `BulkOperationWorker` resolves each id to its collection (`CollectionBookId.HashOf`), deduplicates, sets
  the choice by the same rule as `Detail.cshtml.cs` (`split == librarySplits ? null : split`) and calls the relink the
  page uses. Move that rule into one place (a small static in Core or Data) so the page and the worker share it.
- The filter is a query parameter on `TorrentListQuery`, applied to collection rows (`CollectionTorrentId == null`).
- No migration: the column exists.

## Tests

- Worker: each of the three choices on a collection whose library splits and one whose library doesn't; a book's id
  acts on its collection once; an audiobook is passed over; the relink happens (books removed or added).
- Page: the filter's three values; the bulk dropdown posts the choice; an unknown choice is refused as an unknown
  category is.
- Detail page: the "chosen for this torrent" line.

## Docs

`docs/user-guide.md`, *Collections*: the bulk action, the filter, and how to put a whole library back to what its
`split_collections` says.
