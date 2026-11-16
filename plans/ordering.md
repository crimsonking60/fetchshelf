# Recommended order of the open plans

Written 2026-11-11 and updated 2026-11-16, for the 27 plans in `plans/` (not `plans/done/`). Since the first version,
`bonus-points.md` was built (PR #62) and moved to `plans/done/`. Three plans are new: `language-aware-books.md`,
`series-layout-for-comics.md` and `collection-torrents.md`, the last of them parked. The order follows four rules:

1. **Protect first.** Ten of the plans add a database migration. The before-upgrade backup goes in before them.
2. **Make room before building.** Almost every plan adds settings or registrations to `Program.cs`. Splitting it
   first spares each later plan the 600-line file, and the merge conflicts between them.
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
| MAM's bookmark endpoint; whether quotes make a phrase in author/series/narrator search | `search-bookmark-and-more-like-this.md` | whether the bookmark part is built |
| Hardcover's `editions { isbn_10 isbn_13 asin }` fields and their cost | `hardcover-matching.md` | `editions` or the default editions |
| Audiobookshelf's `/api/libraries/{id}/series` answer | `follow-audiobookshelf-series.md` | the parser |
| The fields Komga's `series.json` parser requires | `series-layout-for-comics.md` | what `MylarSeriesJsonBuilder` writes |
| The Transmission RPC version that has `labels` | `other-download-clients.md` | the minimum version |
| The Windows build spike (step 1 of that plan) | `5-Windows-version.md` | Razor Pages in the Windows build, or a class library |
| How many collection torrents MAM still carries | `collection-torrents.md` | whether the plan is unparked |

## Phase 1: foundations

1. **`database-backup-and-export.md`** — the before-upgrade backup protects users from every migration that follows,
   and a scheduled backup is the only way back from a bad upgrade today.
2. **`composition-root.md`** — no behaviour change; everything after it registers its runner, settings and jobs in
   `Hosting/` instead of `Program.cs`. It also does step 2 of the Windows plan.
3. **`mam-gate-on-pages.md`** — fixes a race that can queue the same book twice, and adds the page gate the bookmark
   toggle uses later.

## Phase 2: correctness of what is there

4. **`hardcover-matching.md`** — fixes a real bug (a list book whose selection is removed is never searched again),
   and adds ISBN/ASIN matching and the back-off. Done before file lists, so they inherit all three through the shared
   importer.
5. **`per-block-budgets.md`** — `wedge` downloads are held back by a ratio budget they never spend, which since the
   budget fix often stops them entirely.
6. **`link-and-tag-details.md`** — the hardlink fallback hides the real error; the Link backlog is invisible; tag
   rules apply without a preview.
7. **`safe-folder-names.md`** — long names fail to link; shared and Windows-formatted disks get mangled names. Before
   the language-aware folders, the series layout, the library check and the Windows build, which all build on the
   folder-name rules.

**Release point:** phases 1 and 2 are fixes and groundwork with nothing to configure for most users.

## Phase 3: seeing and protecting

8. **`account-snapshots.md`** — small, and its history only starts on the day it ships, so the earlier the better.
9. **`allowed-hosts-and-opds-login.md`** — small security hardening.
10. **`seeding-obligations.md`** — shows which torrents are unsatisfied and holds `on_cleaned` back for them: the
    thing that keeps accounts out of hit-and-run trouble.
11. **`jobs-page-dry-run.md`** — makes every block that follows (file lists, bookmarks list, the Audiobookshelf
    series sync, the per-language Follow searches) easy to try without editing the config twice and restarting.
12. **`email-notifications.md`** — small and self-contained.

**Release point.**

## Phase 4: growing the library

13. **`file-based-lists.md`** — turns the Hardcover importer into the shared `ListImporter`.
14. **`mam-bookmarks-list.md`** — needs the shared importer from step 13.
15. **`follow-audiobookshelf-series.md`** — completes series that predate Fetchshelf.
16. **`language-aware-books.md`** — after step 15, since both change the Follow job, and after `safe-folder-names.md`,
    whose template can then express `language_in_folder = "other"`, so only the clash rule is new. After
    `jobs-page-dry-run.md` too: the first searches after the upgrade can list many other-language finds, and a dry
    run from the Jobs page shows them first.
17. **`search-bookmark-and-more-like-this.md`** — "more like this" always; the bookmark toggle if step 0 found the
    endpoint. Needs the page gate from step 3.
18. **`download-client-and-save-path.md`** — after file lists so `[[file_list]]` gets `client` and `save_path` with
    the other blocks; before other download clients, which reuse its client names.

**Release point.**

19. **`series-layout-for-comics.md`** — after `safe-folder-names.md` (the series layout uses its name rules, and
    refuses its `folder_template`) and `link-and-tag-details.md`, which change the same part of the Link job. Before
    the covers, so they can be written in Komga's names in a series folder rather than as one `cover.jpg` per book
    folder, and before the library check, which has to compute series folders too. No migration.
20. **`covers-from-mam.md`** — after `safe-folder-names.md`, `link-and-tag-details.md` and the series layout, which
    change the same part of the Link job.
21. **`statistics-and-metrics.md`** — after `allowed-hosts-and-opds-login.md` (`/metrics` follows `allowed_hosts`)
    and `account-snapshots.md` (the page links to the history).
22. **`freeleech-awareness.md`** stage 2 — only if step 0 found a way to tell when a window is on.

**Release point.**

## Phase 5: the large ones

23. **`25-Library-check.md`** (#25) — after `safe-folder-names.md` and the series layout, since it compares folders
    with the names the Link job would compute, templates and series folders included. Its open questions have
    proposed answers to confirm first.
24. **`5-Windows-version.md`** (#5) — its step 2 is done by `composition-root.md`, and its folder-name rules by
    `safe-folder-names.md`. Its step 3 widens the hardlink fallback for Windows, while `link-and-tag-details.md`
    narrowed it on Linux to the errors that mean "not possible here": the Windows errors join that list rather than
    the fallback catching everything again.
25. **`other-download-clients.md`** — a large refactor that moves every qBittorrent user behind an interface, then
    Transmission; Deluge and rTorrent only when asked for. Last of the building plans, because it touches the most
    code and nobody has asked for it yet.
26. **`library-explorer.md`** — the plan itself says to decide first whether Audiobookshelf already covers it. Decide
    after the rest; build only if the answer is yes.

## Parked

- **`collection-torrents.md`** — waits for MAM's retirement of collection torrents (step 0). If it is unparked, it
  goes in phase 5 before `other-download-clients.md`: both change every place that uses a torrent's id as a
  qBittorrent hash, and the refactor is easier with the collection's mapping already in place. It comes after the
  series layout, which then splits packs into numbered books instead of keeping their file names, and after
  `language-aware-books.md`, whose per-language series ownership the collection's books inherit.

## Dependencies

| Plan | Needs first | Better after |
|---|---|---|
| `mam-bookmarks-list.md` | `file-based-lists.md` | |
| `search-bookmark-and-more-like-this.md` | `mam-gate-on-pages.md` | |
| `other-download-clients.md` | `download-client-and-save-path.md` | `composition-root.md`, `collection-torrents.md` (if unparked) |
| `5-Windows-version.md` | | `composition-root.md`, `safe-folder-names.md`, `link-and-tag-details.md` |
| `25-Library-check.md` | | `safe-folder-names.md`, `link-and-tag-details.md`, `series-layout-for-comics.md` |
| `file-based-lists.md` | | `hardcover-matching.md`, `per-block-budgets.md` |
| `download-client-and-save-path.md` | | `file-based-lists.md` |
| `statistics-and-metrics.md` | | `allowed-hosts-and-opds-login.md`, `account-snapshots.md` |
| `covers-from-mam.md` | | `safe-folder-names.md`, `link-and-tag-details.md`, `series-layout-for-comics.md` |
| `language-aware-books.md` | | `safe-folder-names.md`, `follow-audiobookshelf-series.md`, `jobs-page-dry-run.md` |
| `series-layout-for-comics.md` | | `safe-folder-names.md`, `link-and-tag-details.md` |
| `collection-torrents.md` | | `series-layout-for-comics.md`, `language-aware-books.md` |
| `jobs-page-dry-run.md` | | `composition-root.md` |
| every plan with a migration | | `database-backup-and-export.md` |
| every plan that adds settings or jobs | | `composition-root.md` |

## Plans that touch the same code

Done in the order above, each builds on the one before; done in parallel, they conflict here:

- **`LinkRunner`:** `link-and-tag-details.md`, `safe-folder-names.md`, `language-aware-books.md` (the clash rule),
  `series-layout-for-comics.md`, `covers-from-mam.md`, the library check, `collection-torrents.md`.
- **`FolderNameComputer` and `SyncMismatch`:** `safe-folder-names.md`, `language-aware-books.md`,
  `series-layout-for-comics.md`, the library check. A series library holding one series in two languages puts both
  in one series folder; whether `language_in_folder` applies to series folders is decided by whichever of the two
  plans comes second.
- **`LibraryFileRemover` and `Cleaner`:** `series-layout-for-comics.md` (`series.json`), `covers-from-mam.md`
  (`cover.png`, `cover.webp`), `seeding-obligations.md` (`on_cleaned`), `collection-torrents.md`.
- **`FollowRunner` and `LibraryQueries`:** `follow-audiobookshelf-series.md`, `language-aware-books.md`,
  `collection-torrents.md`.
- **`HardcoverListImporter` / `ListImporter`:** `hardcover-matching.md`, `per-block-budgets.md`,
  `file-based-lists.md`, `jobs-page-dry-run.md`, `mam-bookmarks-list.md`.
- **`DownloadRunner`:** `per-block-budgets.md`, `download-client-and-save-path.md`, `other-download-clients.md`.
- **`SelectionRunner` and `SelectedTorrentEntity`:** `per-block-budgets.md` (`StartedWithWedge`),
  `download-client-and-save-path.md` (`ClientName`, `SavePath`).
- **The Search page:** `mam-gate-on-pages.md`, `search-bookmark-and-more-like-this.md`,
  `download-client-and-save-path.md` (the qBittorrent choice).
- **Every user of a torrent id as a qBittorrent hash:** `collection-torrents.md`, `other-download-clients.md`.
