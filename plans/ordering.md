# Recommended order of the open plans

Written 2026-11-11 and updated 2026-11-19, for the 3 plans in `plans/` (not `plans/done/` or `plans/parked/`). Since the first version,
`bonus-points.md` (PR #62), `database-backup-and-export.md` (PR #65), `composition-root.md` (PR #66),
`mam-gate-on-pages.md` (PR #67), `hardcover-matching.md` (PR #70), `per-block-budgets.md` (PR #71),
`link-and-tag-details.md` (PR #72), `safe-folder-names.md` (PR #73), `account-snapshots.md` (PR #78),
`bulk-actions-in-background.md` (PR #79), `allowed-hosts-and-opds-login.md` (PR #80), `seeding-obligations.md`
(PR #88), `jobs-page-dry-run.md` (PR #89), `file-based-lists.md` (PR #90), `mam-bookmarks-list.md` (PR #91), `series-layout-for-comics.md` (PR #92), `follow-audiobookshelf-series.md` (PR #93), `language-aware-books.md` (PR #96),
`search-bookmark-and-more-like-this.md` (PR #97), `download-client-and-save-path.md` (PR #98), `covers-from-mam.md`
(PR #101), `statistics-and-metrics.md` (PR #102) and `25-Library-check.md` (PR #103) were built and moved to `plans/done/`. `collection-torrents.md` is new: it was parked for a while and taken up again on
2026-11-18. The order follows four rules:

1. **Protect first.** Three of the plans add a database migration. The before-upgrade backup from
   `database-backup-and-export.md` is in place (PR #65), so each of them is backed up before it upgrades a database.
2. **Make room before building.** Almost every plan adds settings or registrations. Since `composition-root.md`
   (PR #66) they go into `Hosting/`, one registration method per area, and a runner's new setting into its options
   record, so later plans no longer meet in one 600-line `Program.cs`.
3. **Fix what exists before adding to it.** Bugs and wrong budgets in current features come before new features.
4. **Respect the dependencies** listed at the end. Where two plans touch the same code, the one that changes how it
   works goes first, so the other builds on the result.

Each step is one plan, one feature branch and one pull request, as usual. Suggested release points are marked.

## Step 0: checks that need no code (any time, the sooner the better)

One plan starts with a check against an outside service. Its answer decides how the plan is built, so it is worth
doing before the plan comes up:

| Check | Plan | Decides |
|---|---|---|
| The Transmission RPC version that has `labels` | `other-download-clients.md` | the minimum version |

## Phase 1: foundations (done)

The backup before upgrades (PR #65), the split of `Program.cs` into `Hosting/` (PR #66), and the MAM gate on the
Search page (PR #67), with the `MamPageGate` the bookmark toggle uses.

## Phase 2: correctness of what is there (done)

- `hardcover-matching.md` (PR #70): the lost-selection fix, ISBN/ASIN matching and the back-off, which file lists
  inherit through the shared importer.
- `per-block-budgets.md` (PR #71): buffers and caps for every grab block, and `wedge` downloads outside the ratio
  budget. File lists get their buffers and cap by their job name, as Hardcover lists do.
- `link-and-tag-details.md` (PR #72): the hardlink fallback only where a hardlink isn't possible, the Link job's
  backlog in its result, and a preview before a tag rule is applied.
- `safe-folder-names.md` (PR #73): folder names cut to 255 bytes, and `folder_names = "portable"` and
  `folder_template` per library. The language-aware folders, the series layout, the library check and, if it is taken up, the Windows
  build build on these.

**Release point (reached):** phases 1 and 2 are fixes and groundwork with nothing to configure for most users.

## Phase 3: seeing and protecting (done)

Done:

- `account-snapshots.md` (PR #78): one snapshot of the account a day, shown over time on the Account page.
- `bulk-actions-in-background.md` (PR #79): bulk actions on the Torrents page run in the background with their
  progress shown, and a library write gate keeps them, the Link job and the Clean job off the same files.
- `allowed-hosts-and-opds-login.md` (PR #80): `allowed_hosts` against DNS rebinding, and an optional OPDS login.
- `seeding-obligations.md` (PR #88): the Seeding job estimates from qBittorrent's seeding time which torrents still
  need seeding (189 against MAM's 190 on the live account), and holds `on_cleaned` back for them.
- `jobs-page-dry-run.md` (PR #89): **Dry run** on the Jobs page (`dry_job_buttons`), and what each run selected listed
  in its history. The blocks that follow (file lists, bookmarks list, the Audiobookshelf series sync, the per-language
  Follow searches) register their dry run the same way.

`email-notifications.md` was parked (see below). Phase 3 is done.

**Release point (reached).**

## Phase 4: growing the library

1. **`file-based-lists.md`** — done (PR #90): `[[file_list]]`, and the Hardcover importer turned into the shared
    `ListImporter`, which takes a `ListDefinition` and an `IListBookSource`.
2. **`mam-bookmarks-list.md`** — done (PR #91): `[[bookmarks_list]]`, the MAM bookmarks as a list, read through the
    search with no request per bookmark.
3. **`follow-audiobookshelf-series.md`** — done (PR #93): `[follow] audiobookshelf_series` follows the series in
    Audiobookshelf, and the books it has count as owned.
4. **`language-aware-books.md`** — done (PR #96): a book in two languages is two books in the Follow job, and
    `language_in_folder` keeps two editions of a title out of one library folder.
5. **`search-bookmark-and-more-like-this.md`** — done (PR #97): authors, narrators and series on the Search page link
    to a search for more of the same, the name as a quoted phrase. No bookmarking: MAM authorizes no bookmark endpoint.
6. **`download-client-and-save-path.md`** — done (PR #98): `client` and `save_path` on every block and `[[tag]]` rule
    choose the qBittorrent, by its `name`, and the folder a download goes to; the Search page offers the choice too.

**Release point.**

7. **`series-layout-for-comics.md`** — done (PR #92): `layout = "series"`, one folder per series and one numbered
    file per book, with a Mylar `series.json`, for Komga.
8. **`covers-from-mam.md`** — done (PR #101): `covers_from_mam` is the number of covers each Link run fetches from MAM
    for book folders without an image. Series libraries are left out: comics carry their cover inside the file.
9. **`statistics-and-metrics.md`** — done (PR #102): a Statistics page with the library's totals and the grabs by
    month, cost and grabber, and `metrics = true` for a Prometheus `/metrics`.

**Release point.**

## Phase 5: the large ones

10. **`25-Library-check.md`** (#25) — done (PR #103): the Library check job and page list unknown folders and files,
    and books whose folder or files are gone, each with what to do about it.
11. **`collection-torrents.md`** — splits a collection into one library book each. Taken up again on 2026-11-18:
    comic collections are still common, such as an Ultimate Spider-Man collection that MAM gives no series and whose
    145 `cbr` issues lost to its one `cbz` annual. Its "Collections without a series range" section has two gaps to
    settle first: choosing the format per book rather than per torrent, and detecting a collection from its file names.
    After the series layout, which places the split books as numbered files, and after `language-aware-books.md`
    (PR #96), whose per-language series ownership and `language_in_folder` the books inherit. Before `other-download-clients.md`: both change every place
    that uses a torrent's id as a qBittorrent hash, and the refactor is easier with the collection's mapping in place.
12. **`other-download-clients.md`** — a large refactor that moves every qBittorrent user behind an interface, then
    Transmission; Deluge and rTorrent only when asked for. Last of the building plans, because it touches the most
    code and nobody has asked for it yet.
13. **`library-explorer.md`** — the plan itself says to decide first whether Audiobookshelf already covers it. Decide
    after the rest; build only if the answer is yes.

## Parked

Parked plans are left out of the order until they are taken up again.

- **`parked/email-notifications.md`** — moved to `plans/parked/` on 2026-11-17.
- **`parked/freeleech-awareness.md`** — moved to `plans/parked/` on 2026-11-18: MAM's API has no way to tell when a
  freeleech window is on.
- **`parked/5-Windows-version.md`** (#5) — moved to `plans/parked/` on 2026-11-19: not built until someone who wants a
  Windows version, and can test it, turns up.

## Dependencies

| Plan | Needs first | Better after |
|---|---|---|
| `other-download-clients.md` | `download-client-and-save-path.md` (done, PR #98) | `collection-torrents.md` |

## Plans that touch the same code

Done in the order above, each builds on the one before; done in parallel, they conflict here:

- **`LinkRunner`** (it takes the library write gate from PR #79 per torrent, and the language clash rule from PR #96;
  it sets `CoverCheckedAt` back on every placement since PR #101, and `RelinkBlockerAsync` mirrors Relink's checks for the
  Library check since PR #103): `collection-torrents.md`.
- **`LibraryFileRemover` and `Cleaner`** (the Clean job holds `on_cleaned` back for unsatisfied copies since PR #88, and
  every cover name is an extra since PR #101): `collection-torrents.md`.
- **`FollowRunner` and `LibraryQueries`** (Audiobookshelf's books count as owned since PR #93, and ownership is per
  language since PR #96): `collection-torrents.md`.
- **Every user of a torrent id as a qBittorrent hash:** `collection-torrents.md`, `other-download-clients.md`.
