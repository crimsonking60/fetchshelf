# Watched lists

## Problem

A `[[hardcover_list]]` (and a `[[file_list]]` or `[[bookmarks_list]]`) selects every book it finds, as far as its grab
blocks and budgets allow. Nothing in between is possible: the list either downloads all its books, or it isn't
configured. A list kept by someone else, such as an award's winners, is more useful to look through than to download
in full.

A followed series or author has this already: in *Follow* mode the Follow job only lists what it finds, and the
Following pages offer **Download**, **Wedge download**, **Dismiss** and **Undo dismiss**, one by one or for the ticked
finds in the background. Lists get the same.

## Design

Lists stay lists: their books keep being read, matched and stored as now, one `ListItems` row per book with an
audiobook and an ebook side. Making a list a third kind of follow was considered and left aside: a follow is one MAM
search by name with one row per torrent, while a list searches per book, by identifiers, with preferred formats and
books that aren't on MAM yet, all of which the Lists page shows already.

1. **A `mode` setting.** `[[hardcover_list]]`, `[[file_list]]` and `[[bookmarks_list]]` get
   `mode = "auto"` (the default, as today) or `mode = "watch"`.
   - `ListMode` enum in the configuration, carried as `ListDefinition.Mode`.
   - Grab blocks are still required in watch mode: they decide which torrents count as the book, and which costs a
     find is offered at.
   - `docs/configuration.md` gets the row for each list kind; `5-everything.toml` uses it.

2. **Two new side statuses.** `ListItemSideStatus` gains:
   - `Found`: a match waiting to be looked at, in watch mode. Not resolved, so a later search may replace it with a
     better torrent, as a follow's search does for a *New* find.
   - `Dismissed`: put aside; resolved, so the side is never searched for again.
   The statuses are strings, so no migration is needed.

3. **The list job in watch mode.** `ListImporter.RecordSideAsync` records a winner as `Found` with its MAM id instead of
   calling `SelectionRunner.ProcessCandidateAsync`. Everything else stays:
   - A match no grab block's cost accepts is still `Wanted`, and one in the library still `Existing`.
   - `prefer_format` works as now: when both formats are found, the other one is `NotWanted`.
   - The run's budget and `max_active_downloads` don't limit what is found, as nothing is selected; the "left for the
     next run" case doesn't arise.
   - The run's summary says "found" instead of "selected"; a dry run writes nothing, as now.
   - A side that is `Found` and turns up in the library becomes `Existing`, as any unresolved side does.

4. **`ListService`** (data layer, beside `FollowService`):
   - `DownloadAsync(listId, externalId, side, wedge, ct, waitForMam)`: asks MAM for the side's torrent again, so the
     cost is today's, and selects it through `SelectionRunner` with the list's placement, buffers and job name, at
     `ConfiguredCost.Ratio` or `ConfiguredCost.Wedge`, as a find's Download does. It takes `MamAccessGate` the same
     way: `AcquireAsync` for a bulk action, `TryAcquireAsync` with the page wait for a button.
     - Selected → `Selected` (and the other side `NotWanted` under `prefer_format`), in the library → `Existing`,
       else the status stays and the outcome is described.
     - It works for a `Found` side and for a `Wanted` one, in either mode: a wanted book can then be wedged by hand.
   - `DismissAsync` and `UndoDismissAsync` for a side. Undo puts it back to `Found` when it has a MAM id, else clears
     the status so the next run searches for it.
   - After a selection, the Download job is started as after a find's Download (`download_after_selection`).

5. **The list's page.**
   - A side that is `Found` or `Wanted` gets **Download** and **Wedge download**; a `Found` one also **Dismiss**,
     a `Dismissed` one **Undo dismiss**. *Mark done* and *Search again* stay as they are.
   - A "To look at" filter: books with a `Found` side.
   - Tick boxes and the bulk bar as on the Following pages (`bulk-bar.js`, `_BulkOperationsPartial`), running in the
     background through a new `BulkOperationPage.ListItems` and a `ListItemsBulkRunner` modelled on
     `FindsBulkRunner`. An item id is the book's external id and its side; the operation's origin is the list id.
   - The Lists page shows, per list, its mode and how many books are waiting to be looked at.

6. **Docs and README.** The user guide's Lists section describes watch mode and the buttons; the README's Untested
   list gets "Watched lists".

## Left out

- The Following pages' *To look at* page doesn't show list finds; only the Lists pages do.
- One mode per list; no per-book switch.
- No notification for new finds of a watched list.

## Changing a list's mode

- Auto → watch: what was selected stays selected; unresolved books are found, not selected, from then on.
- Watch → auto: `Found` sides are selected at the next run, as a follow switched to *Auto* does; `Dismissed` ones
  stay dismissed.

## Tests

- `ListImporter`: watch mode records `Found` and selects nothing; a later better match replaces a `Found` torrent;
  `Dismissed` isn't searched for; watch → auto selects the found sides.
- `ListService`: download of a found and of a wanted side, `prefer_format` after a download, dismiss and undo.
- Configuration: `mode` read for each list kind, an unknown value refused.
- Web: the list page's buttons, the bulk bar posting to its own page, and a bulk run's summary.
