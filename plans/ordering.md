# Recommended order of the open plans

Written 2026-11-11 and updated 2026-11-18, for the 13 plans in `plans/` (not `plans/done/` or `plans/parked/`). Since the first version,
`bonus-points.md` (PR #62), `database-backup-and-export.md` (PR #65), `composition-root.md` (PR #66),
`mam-gate-on-pages.md` (PR #67), `hardcover-matching.md` (PR #70), `per-block-budgets.md` (PR #71),
`link-and-tag-details.md` (PR #72), `safe-folder-names.md` (PR #73), `account-snapshots.md` (PR #78),
`bulk-actions-in-background.md` (PR #79), `allowed-hosts-and-opds-login.md` (PR #80), `seeding-obligations.md`
(PR #88), `jobs-page-dry-run.md` (PR #89), `file-based-lists.md` (PR #90) and `mam-bookmarks-list.md` (PR #91) were built and moved to `plans/done/`. Three plans are new:
`language-aware-books.md`, `series-layout-for-comics.md` and `collection-torrents.md`; the last was parked for a while and
taken up again on 2026-11-18. The order follows four rules:

1. **Protect first.** Five of the plans add a database migration. The before-upgrade backup from
   `database-backup-and-export.md` is in place (PR #65), so each of them is backed up before it upgrades a database.
2. **Make room before building.** Almost every plan adds settings or registrations. Since `composition-root.md`
   (PR #66) they go into `Hosting/`, one registration method per area, and a runner's new setting into its options
   record, so later plans no longer meet in one 600-line `Program.cs`.
3. **Fix what exists before adding to it.** Bugs and wrong budgets in current features come before new features.
4. **Respect the dependencies** listed at the end. Where two plans touch the same code, the one that changes how it
   works goes first, so the other builds on the result.

Each step is one plan, one feature branch and one pull request, as usual. Suggested release points are marked.

## Step 0: checks that need no code (any time, the sooner the better)

Several plans start with a check against an outside service. Their answers decide whether a plan is built at all
or how, so they are worth doing before the plans come up:

| Check | Plan | Decides |
|---|---|---|
| Whether MAM's API reports freeleech windows | `freeleech-awareness.md` | a direct field or detection from search results |
| Whether quotes make a phrase in author/series/narrator search (the bookmark endpoint: checked, there is none) | `search-bookmark-and-more-like-this.md` | whether names are quoted |
| Audiobookshelf's `/api/libraries/{id}/series` answer | `follow-audiobookshelf-series.md` | the parser |
| The fields Komga's `series.json` parser requires | `series-layout-for-comics.md` | what `MylarSeriesJsonBuilder` writes |
| The Transmission RPC version that has `labels` | `other-download-clients.md` | the minimum version |
| The Windows build spike (step 1 of that plan) | `5-Windows-version.md` | Razor Pages in the Windows build, or a class library |

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
  `folder_template` per library. The language-aware folders, the series layout, the library check and the Windows
  build build on these.

**Release point (reached):** phases 1 and 2 are fixes and groundwork with nothing to configure for most users.

## Phase 3: seeing and protecting

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
3. **`follow-audiobookshelf-series.md`** — completes series that predate Fetchshelf.
4. **`language-aware-books.md`** — after step 3, since both change the Follow job. `folder_template` (PR #73) can
    already express `language_in_folder = "other"`, so only the clash rule is new. The
    first searches after the upgrade can list many other-language finds; a dry run from the Jobs page (PR #89)
    shows them first.
5. **`search-bookmark-and-more-like-this.md`** — "more like this" only: MAM authorizes no bookmark endpoint, so the
    bookmark toggle is dropped. Its searches take MAM through `MamPageGate` (PR #67).
6. **`download-client-and-save-path.md`** — after file lists so `[[file_list]]` gets `client` and `save_path` with
    the other blocks; before other download clients, which reuse its client names.

**Release point.**

7. **`series-layout-for-comics.md`** — builds on `safe-folder-names.md` (PR #73): the series layout uses its name
    rules, and refuses its `folder_template`. Before
    the covers, so they can be written in Komga's names in a series folder rather than as one `cover.jpg` per book
    folder, and before the library check, which has to compute series folders too. No migration.
8. **`covers-from-mam.md`** — after the series layout, which changes the same part of the Link job.
9. **`statistics-and-metrics.md`** — `/metrics` follows `allowed_hosts` (PR #80), and its page links to the account
    history (PR #78).
10. **`freeleech-awareness.md`** stage 2 — only if step 0 found a way to tell when a window is on.

**Release point.**

## Phase 5: the large ones

11. **`25-Library-check.md`** (#25) — after the series layout, since it compares folders with the names the Link job
    would compute, `folder_template` (PR #73) and series folders included. Its open questions have
    proposed answers to confirm first.
12. **`5-Windows-version.md`** (#5) — its step 2 was mostly done by `composition-root.md`, and its folder-name rules by
    `safe-folder-names.md` (PR #73), whose `FileNameRules` it only has to make the Windows default. Its step 3 widens
    the hardlink fallback for Windows, while `link-and-tag-details.md` (PR #72) narrowed it on Linux to the errors that
    mean "not possible here": the Windows errors join that list rather than the fallback catching everything again.
13. **`collection-torrents.md`** — splits a collection into one library book each. Taken up again on 2026-11-18:
    comic collections are still common, such as an Ultimate Spider-Man collection that MAM gives no series and whose
    145 `cbr` issues lost to its one `cbz` annual. Its "Collections without a series range" section has two gaps to
    settle first: choosing the format per book rather than per torrent, and detecting a collection from its file names.
    After the series layout, which places the split books as numbered files, and after `language-aware-books.md`,
    whose per-language series ownership the books inherit. Before `other-download-clients.md`: both change every place
    that uses a torrent's id as a qBittorrent hash, and the refactor is easier with the collection's mapping in place.
14. **`other-download-clients.md`** — a large refactor that moves every qBittorrent user behind an interface, then
    Transmission; Deluge and rTorrent only when asked for. Last of the building plans, because it touches the most
    code and nobody has asked for it yet.
15. **`library-explorer.md`** — the plan itself says to decide first whether Audiobookshelf already covers it. Decide
    after the rest; build only if the answer is yes.

## Parked

Parked plans are left out of the order until they are taken up again.

- **`parked/email-notifications.md`** — moved to `plans/parked/` on 2026-11-17.

## Dependencies

| Plan | Needs first | Better after |
|---|---|---|
| `other-download-clients.md` | `download-client-and-save-path.md` | `collection-torrents.md` |
| `25-Library-check.md` | | `series-layout-for-comics.md` |
| `covers-from-mam.md` | | `series-layout-for-comics.md` |
| `language-aware-books.md` | | `follow-audiobookshelf-series.md` |
| `collection-torrents.md` | | `series-layout-for-comics.md`, `language-aware-books.md` |

## Plans that touch the same code

Done in the order above, each builds on the one before; done in parallel, they conflict here:

- **`LinkRunner`** (it takes the library write gate from PR #79 per torrent): `language-aware-books.md` (the clash rule),
  `series-layout-for-comics.md`, `covers-from-mam.md`, the library check, `collection-torrents.md`.
- **`FolderNameComputer`, `FolderLayout` and `SyncMismatch`:** `language-aware-books.md`,
  `series-layout-for-comics.md`, the library check. A series library holding one series in two languages puts both
  in one series folder; whether `language_in_folder` applies to series folders is decided by whichever of the two
  plans comes second.
- **`LibraryFileRemover` and `Cleaner`** (the Clean job holds `on_cleaned` back for unsatisfied copies since PR #88):
  `series-layout-for-comics.md` (`series.json`), `covers-from-mam.md` (`cover.png`, `cover.webp`),
  `collection-torrents.md`.
- **`FollowRunner` and `LibraryQueries`:** `follow-audiobookshelf-series.md`, `language-aware-books.md`,
  `collection-torrents.md`.
- **`DownloadRunner`:** `download-client-and-save-path.md`, `other-download-clients.md`.
- **The Search page:** `search-bookmark-and-more-like-this.md`,
  `download-client-and-save-path.md` (the qBittorrent choice).
- **Every user of a torrent id as a qBittorrent hash:** `collection-torrents.md`, `other-download-clients.md`.
