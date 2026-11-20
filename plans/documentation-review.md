# Documentation review: closing the gaps between docs and code

## Background

All documentation was read against the code as of 2026.11.19 (`8dbfe45`): `docs/configuration.md`,
`docs/user-guide.md`, `docs/getting-started.md`, `docs/fs.md`, `docs/database-setup.md`, `docs/index.md`,
`docs/config-examples/`, `README.md`, the contributor and maintainer guides, `CONTRIBUTING.md`, `SECURITY.md`,
the GitHub templates, the two newest sections of `RELEASES.md`, and the local `CLAUDE.md`. Each claim was
traced to the code that implements it, and the code was walked the other way (settings, environment variables,
command-line modes, pages, endpoints, jobs, metrics, notification events, files written) to find what the docs
leave out.

The review was done by reading; nothing was built or run. The findings that lead to code changes were
re-read in the code afterwards. Line numbers refer to `8dbfe45`.

### Overall result

The docs are in good shape. Every setting in the config model except one deliberate one (`development_banner`)
is in the reference, every default and range matches, all 16 metrics, 12 notification events, 63 languages,
the media types, categories and flags match, and every snippet and example file loads. The gaps fall into
four groups:

1. Places where the docs describe the sensible behaviour and the code does something else (part A).
2. Sentences that went stale when later features arrived: `[[file_list]]`, `[[bookmarks_list]]`, `[follow]`,
   `[upgrade]`, `[bonus]`, `save_path`, `client`, `[[notify]]`, Hardcover (part B).
3. Wrong details: labels, column names, defaults in example comments (part B).
4. Small undocumented behaviour, most of it better left alone (part C).

Five items need a decision before work starts (part D).

## Part A: the docs are right, the code changes

Each item gets a test that fails before the change.

### A1. A list book without an author never matches

- Docs: `user-guide.md:1159`, `:1195-1196`, `configuration.md:738`, `config-examples/wanted-books.txt:2` offer a
  title-only line, "matched on the title alone".
- Code: `ListLibraryMatcher.FindMatch` (`Core.AutoDownloader/ListLibraryMatcher.cs:36-41`) requires a matching
  author, which an empty author list never gives. `ListMamQueryBuilder.Build` (`:50-55`) sends
  `@author ()`. Such a book stays *not found* and costs a MAM search daily, then weekly, for ever.
- Change: with no authors, match on the title alone and leave the `@author` clause out. The docs already warn
  that this finds the wrong book more often.

### A2. Hand edits to a book of a split collection are not used to place it

- Docs: `user-guide.md:1677-1678`: Edit, then **Refresh and relink** "moves it to match".
- Code: `LinkRunner.cs:421-422` computes the folder and writes the files from the splitter's metadata; the
  hand edits are merged into the record only afterwards (`:440-445`). The folder follows the splitter while
  the Torrents page shows the edit.
- Change: apply `HandEdits.KeepOver` before `BookFolderAsync` and `PlaceFiles` for a known book.

### A3. Bulk **Refresh selected** replaces hand edits without asking

- Docs: `user-guide.md:1987`: Refresh "on a hand-edited torrent … asks first".
- Code: the confirmation exists only on the torrent page (`Torrents/Detail.cshtml:160-162`). The bulk button
  (`Torrents/_TorrentListPartial.cshtml:97`) has none, and `BulkActionRunner.cs:103` calls `RefreshAsync`
  directly.
- Change: the bulk Refresh skips hand-edited torrents and says how many were skipped in the operation's
  result; the torrent page stays the place to overwrite an edit. One sentence in the guide.

### A4. Required settings that are never checked

- `[audiobookshelf]` `url` and `token` (`configuration.md:320-321`): `ValidateAudiobookshelf`
  (`ConfigLoader.cs:719`) returns early without a `path_mapping` and never looks at them. An empty section
  starts and fails at run time.
- `[[library]]` `library_dir` (`configuration.md:1107`): `ValidateLibraries` (`ConfigLoader.cs:519`) does not
  check it. A library without it links into a path relative to the working directory.
- Change: refuse both at startup, with the section named.

### A5. Hardcover list blocks that collide

- Two `[[hardcover_list]]` blocks on the same `status` or `list` share one stored list
  (`ListDefinition.cs:138-139`, the id does not include the name), each run overwriting the other's name,
  `prefer_format` and per-format state. Refuse it at startup and add a sentence at `configuration.md:628`.
- `list = ""` beside a `status` passes validation (`IsNullOrWhiteSpace`, `ConfigLoader.cs:830`) but runs as a
  list with an empty slug (`is { } slug`). Treat a blank `list` as unset in the loader.

### A6. Library check page can't be reached without findings

- Docs: `user-guide.md:1811`: "always reachable from the job on the **Jobs** page".
- Code: the link is shown only with findings (`Jobs/_JobListPartial.cshtml:97`), the same condition as the menu
  entry. Before the first check and after a clean one, the page is reachable only by its address.
- Change: show the link on the Library check row always (worded *Findings* only when there are some).

### A7. **Run now** on the Config page shows a bare fragment

- `Config/Index.cshtml:180` posts a plain form to `/Jobs?handler=Trigger`, which always answers with the
  job-list partial (`Jobs/Index.cshtml.cs:47-58`). The job starts, but the browser shows an unstyled fragment.
- Change: redirect to the Jobs page when the request is not an htmx one. To be confirmed in a browser first.

### A8. List page explanation contradicts the search schedule

- `Lists/Detail.cshtml:10` says "searched for again once a day"; `ListSearchSchedule` and
  `user-guide.md:2108-2109` say daily for a week, then weekly. Change the page text.

### A9. `--check-config` summary leaves jobs out

- Docs: `configuration.md:108`: the summary lists "every job".
- Code: `ConfigSummary.cs:134-193` has no row for Follow, Upgrade, Bonus or Library check, and only a settings
  line for Seeding and Backup. Add the rows, so the summary shows that `[follow]`, `[upgrade]` and `[bonus]`
  were understood.

### A10. Smaller code fixes

- **`media_type` parsing** (`SharedFilterConfigMapper.cs:67`): a bare `Enum.TryParse` also accepts numbers and
  comma-joined names (`"99"`, `"Ebook, Manga"`). Match names only, as the neighbouring parsers do.
- **Validation errors that don't name the section** (`ConfigLoader.cs:468-487`, `:526-536`, `:602-639`):
  "a [[autograb]] section" where `configuration.md:57-59` promises the name or position. Use
  `ConfigValueValidator.Label`.
- **`metadata.json` language** (`LibraryMetadataJsonBuilder.cs:31`, also `AbsClient.cs:114`): writes the enum
  name (`BrazilianPortuguese`). Use the display name ("Brazilian Portuguese"), as folder names do.
- **Log files** (`FetchshelfLogging.cs:43`): the file sink runs on Serilog's defaults, 31 files kept and a
  day's file silently stopping at 1 GB. Set both explicitly (roll on size) and state the file name and the
  number kept at `user-guide.md:173`. An unknown `FETCHSHELF_LOG` value is ignored without a word; log a
  warning.
- **Jobs page "is not configured" list** (`Jobs/UnconfiguredJobs.cs:13-21`): add Follow, Upgrade, Bonus and
  Backup, which the guide's "each kind of optional job" (`user-guide.md:2155`) implies.
- **Config page**: no `[audiobookshelf]` or `[hardcover]` section and no `download_after_selection`, against
  "the settings in effect" (`user-guide.md:2122`). Add them, secrets shown as set or not.
- **Library check**: **Delete selected** removes files from disk without a confirmation
  (`LibraryCheck/Index.cshtml:111`). Add one, as Unlink has.
- **`utils/fs`**: knows 6 of 9 event kinds and 3 of 10 error stages (`utils/fs:23-24`), so newer rows print a
  bare number, `--kind`/`--stage` can't select them and `fs stats` undercounts errors; `--status` lacks
  `no-grab-block`; `--state` and `--stage` are validated loosely but compared exactly, so `--state Queued`
  matches nothing although `fs.md:69-70` says case doesn't matter. Extend the lists, normalize the two filters,
  then update `fs.md` (B8).

## Part B: the code is right, the docs change

### B1. Stale since later features arrived

**List kinds and blocks left out of enumerations in `configuration.md`.** The code treats every list kind,
`[follow]` and `[upgrade]` alike; these sentences name only autograbs and Hardcover lists. A phrase such as
"every list" covers most of them.

| Line | Subject | Missing |
|---|---|---|
| 62 | filter validation | `[[follow.grab]]`, `[upgrade]` grab blocks |
| 71-73 | "one exception" to section overrides | the four `FETCHSHELF_CONF_opds_*` (documented at 404) |
| 119-125 | diagram | file list, bookmarks list, upgrade |
| 131, 150, 451 | `category` | file and bookmarks lists |
| 136, 212-213 | per-block buffers | file and bookmarks lists |
| 215 | `download_after_selection` | file lists, bookmarks list, Follow, Upgrade, Following page |
| 228, 470 | `languages` inheritance | `file_list.grab`, `bookmarks_list.grab`, `follow.grab` |
| 235 | `dry_job_buttons` | file and bookmarks lists |
| 467 | name rules | file and bookmarks lists; `hardcover_list[n]`, `file_list[n]`, `bookmarks_list` |
| 591 | where `cost` appears | file and bookmarks lists |
| 761 | `[[bookmarks_list]] name` | follows the autograb name rules |

**`save_path` and `client` in the user guide.**
- `user-guide.md:305-306`, `:472-473`: "Fetchshelf does not choose where downloads are saved". It does when a
  block or `[[tag]]` rule sets `save_path`; the guide never mentions the setting. Reword and link to the
  reference; the `path_mapping` advice depends on it.
- `user-guide.md:453`, `:2582`, `5-everything.toml:97`: "New downloads go to the first `[[qbittorrent]]`". Only
  without a `client` setting or a Search page choice, as `:853-856` says.
- `user-guide.md:853`: the "in qBittorrent" chooser lists only blocks that have a `name`.

**Background jobs (`user-guide.md:2135-2204`).**
- The jobs table (`:2139-2151`) has no row for Follow, Upgrade or Bonus (`bonus_interval`, default 120).
- Start-up order (`:2202-2203`): Seeding also starts with Link; Follow, Upgrade and Bonus come between the
  lists and Download; the order is autograbs, Hardcover lists, file lists, bookmarks list.
- Job history triggers (`:2190`): add *Dry run*.
- "An interval of `0` means the job never runs by itself" (`:2176`, `:2428`, `:2579`): say "on a schedule";
  Link still starts when a download finishes, Download after a selection, Clean after Link.
- "Is not configured" (`:1802`, `:2155`): the list closes the *Configured jobs* group, it is not below the jobs.
- FAQ "How do I stop it downloading?" (`:2566-2568`): lists, `[follow]` and `[upgrade]` have their own
  `search_interval`, and `[bonus]` keeps spending on `bonus_interval`.

**Errors and Events.**
- Errors: Follow and Upgrade record entries too (`:1810`, `:2016-2026`, `:2184`).
- Events row (`:1809`): add bonus purchases.

**Security and privacy (`user-guide.md:2376-2400`).** This is what a careful user reads before sharing a log.
- Secrets (`:2388`, `:2602`): add the Hardcover token and the `[[notify]]` `url` and `token`.
- Outbound (`:2392-2393`): add Hardcover, the notify services (which receive book titles and job names) and
  MAM's CDN for covers; mention the `User-Agent: Fetchshelf/<version>` sent to MAM and Hardcover.
- Logs (`:2397-2399`): titles are not limited to `debug`. The default level already logs the titles of books
  linked, retired, matched and checked (`AbsMatcher.cs:133`, `Cleaner.cs:333`); `debug` adds every search
  result looked at.
- The same secrets list is short in `contributors-guide.md:786-788`, `CONTRIBUTING.md:33` and
  `.github/ISSUE_TEMPLATE/bug_report.yml:12`. A pasted webhook URL in a bug report is a real leak.

**Notifications.**
- `configuration.md:1399`: `unsatisfied_not_seeding` is a problem too (`NotificationEvent.cs:91-93`).
- `configuration.md:1394`: ntfy also sends `white_check_mark` (recovered) and `information_source` (the rest).
- `user-guide.md:2240`: a failed delivery is retried only on no answer, 5xx or 429; another 4xx is dropped.
- `user-guide.md:2232`: `unsatisfied_not_seeding` is merged too, and a burst is held 5 minutes at most.

**Developer docs.**
- `contributors-guide.md:375-384`: the project table lacks `Fetchshelf.Clients.Hardcover`; the auto-downloader
  row lacks file lists, bookmarks lists and Bonus.
- `contributors-guide.md:347-352`: add `FETCHSHELF_CONF_<setting>`, `FETCHSHELF_SCALE` and
  `development_banner` (see C).
- `contributors-guide.md` Testing: the scale tests (`FETCHSHELF_SCALE=10000 dotnet test --filter
  "Category=Scale"`) and the shared sources under `tests/Shared/`.
- `contributors-guide.md:422-448`: `utils/fs` reads the database directly and no test covers it, so a schema
  or enum change needs a look at it.
- `contributors-guide.md`: name the command-line modes `--check-config` and `--healthcheck`.
- `contributors-guide.md:456-457`: the Account page and the Following page's find-download take
  `MamAccessGate` directly, not through `MamPageGate`.
- `maintainer-guide.md:222-228`: `dotnet ef database update` covers the schema only; the numbered data
  upgrades in `DatabaseMigrator` run at startup. Try a migration by starting Fetchshelf on a copy
  (`FETCHSHELF_DATA_FILE`).
- `contributors-guide.md:277`: the example test is now `…_KeepsOneErrorRowNamedAfterTheTorrent`.

### B2. Wrong statements

- **Default config path.** `configuration.md:43-45` and `contributors-guide.md:349` promise a
  "platform-appropriate config path". There is none: it is `config.toml` in the working directory
  (`ConfigLoader.cs:72-77`).
- **"No library matched" is not an Errors entry.** `user-guide.md:2456` and `getting-started.md:561-567` send
  the reader to the Errors page. A torrent no library takes is skipped silently (`LinkRunner.cs:249-252`) or,
  when Fetchshelf knows it, marked *no library* on the Torrents page. This is the likeliest first-run mistake
  (a wrong `download_dir`), so both places say where to look instead.
- **Quoted error texts.** *Source missing* and *No author* (`getting-started.md:563`, `:568`,
  `user-guide.md:2458`, `2-library-organizer.toml:34`) are paraphrases; the texts are "Cannot link: source file
  not found: …" and "Torrent has no author; cannot be linked." Quote them so a search finds the section.
- **Removing "not on MAM" entries** (`getting-started.md:314-316`): the entry is what holds the next MAM
  lookup back for a day (`LinkRunner.NotOnMamRecentlyAsync`), so a removed one is asked about again on the
  next Link run. Say the entries are best left, or that category-based libraries keep such torrents out.
- **Torrents page** (`user-guide.md:1910`, `:1927-1930`, `:1965`, `:1407`, `:1763`): there is no *Client
  status* or *Mismatch* column. The columns are Title, Format, Category, Status, Added; the mismatch is a badge
  in *Status*. Also: the filters sit in a folded **Filters** panel (`:1900`), and **Set category** sits between
  Relink and Unlink, only when a library has a category (`:1956`).
- **Pagers.** Events (`:2002`) and Search (`:778`): the labels are **Prev** and **Next** with page numbers, as
  `:1925` already says.
- **Search page.** `:809` and `:840`: the columns are Title, Format, Status, Size, Cost; the quality pill sits
  under the media type and the qBittorrent marks beside the status. `:886-889`: **Try again** exists for a
  search and a result's details, not for Download or Import. `:866-867`: Import also shows on a *Selected*
  result and takes it off the queue.
- **`max_active_downloads`** (`user-guide.md:1067`): it caps selections waiting to be downloaded, not
  unfinished downloads (`SelectionQueue.cs:40-46`); `:1370` has it right.
- **Lists** (`user-guide.md:1139-1140`, `configuration.md:748-749`, `5-everything.toml:232-233`): "once a
  day" is daily for a week, then weekly.
- **Follow** (`user-guide.md:1315-1318`): `searches_per_run` limits follows per run, not requests; each reads
  one or more pages. `configuration.md:848`: "however many" is up to 100 pages.
- **Selected** (`user-guide.md:1805`, `:1890`): started selections stay until finished; lists, Follow and
  Upgrade choose too.
- **Account** (`:1824-1825`): before the first read the page still shows Over time, Bonus points and
  Connection to MAM.
- **Library menu order** (`:1790`): Series, Following, Lists, Duplicates, Library check.
- **Start-up message** (`:2415`): the line reads "Cannot start with …: Invalid configuration: …", so "says",
  not "begins with".
- **Tag rules "for torrents you add by hand"** (`:529`): they apply to Search page downloads; a torrent added
  in qBittorrent gets them only through the Config page's apply. `configuration.md:1073-1086`: rules are
  resolved when a torrent is selected, so a changed rule doesn't reach the Selected queue.
- **Ratio budget log** (`configuration.md:251-252`): logged on every run that has something to download.
- **Series libraries** (`configuration.md:1299`): `metadata_json`/`metadata_opf` can't be turned on (`false`
  loads). `:1122-1123`: `oneshots_dir` or `series_json` on a book-layout library stops startup;
  `oneshots_dir` is a single folder name.
- **Clean ranking** (`user-guide.md:1689-1690`): copies chosen from different format lists are ranked with the
  global list (`LibraryCopyRank.cs:27-40`).
- **"Only removes files it linked itself"** (`user-guide.md:1375`, FAQ `:2553`): also `metadata.json`,
  `metadata.opf`, `series.json` and `cover.jpg/.png/.webp` beside them, whoever wrote them
  (`LibraryFileRemover.cs:10`), and Library check's **Delete selected** deletes what is ticked.
- **Dry-run history** (`user-guide.md:1050-1053`): at most 100 lines, 50 of them torrents turned down; the
  log has all.
- **Flattening** (`configuration.md:1149-1150`): files that would clash keep their subfolder as a prefix
  (`Part 1 - 01.mp3`), then ` (2)`.

### B3. Example files

- `1-minimal.toml:21`: "Every few minutes" is every half hour. `:17`, `:31-32`: `localhost` and two separate
  mounts contradict the getting-started guide; use `http://qbittorrent:8080`, `/media/downloads` and
  `/media/library`.
- `2-library-organizer.toml:51-53`: the default method is `hardlink_or_copy`, not `hardlink`. `:23-25`: the
  comment describes `exclude_narrator_in_library_dir = true` above a `false`.
- `4-targeted-searches.toml:85-91`: `metadata_only` doesn't make uploads "count as owned"; only
  `metadata_only_add` creates the record (`SelectionRunner.cs:129-147`). Use that, or reword.
- `5-everything.toml`: claimed to use every setting (`:3`, `configuration.md:5-6`, `user-guide.md:337`,
  `config-examples/README.md:16`). Add what is missing: `covers_from_mam`, `split_collections`, `metrics`,
  `link_method`; `[[qbittorrent]] name`; `client` and `save_path`; autograb `description`; per-list buffers
  and `max_active_downloads`; `[follow] audiobookshelf_series` and `audiobookshelf_libraries`; library
  `folder_template`, `language_in_folder`, `split_collections`; a `[[upgrade.grab]]` block; the full
  `sort_by` (`:145`) and notify event (`:416-417`) lists; "Wedge download" (`:72`).
  A test that fails when the config model gains a property the file lacks would keep the claim true.

### B4. `docs/fs.md` and `docs/database-setup.md`

- `fs.md:29-30`, `:267`: lists are not only Hardcover lists.
- `fs.md:217`, `:252-255`, `:285-287`: the kinds, stages and statuses after A10's change; a `key` is not
  always a MAM id or info-hash.
- `fs.md:312-313`: 7 of 17 tables are listed; say "the main tables".
- `fs.md:171-172`: `--author` and `--series` take one value.
- `fs.md:184-186`: a book of a split collection has the id `<hash>-<n>`, so a hash prefix can be ambiguous.
- `database-setup.md:3-10`: the folder also holds `keys/`, `instance-id` and `covers/`; the backup table at
  `user-guide.md:2325-2330` lacks `covers/` (rebuilt on demand).

### B5. `CLAUDE.md` (local, not tracked)

- `:23` "no release yet": releases exist, so settings and databases in the field now constrain changes.
- `:32`: `Parses_SingleEntry` doesn't exist; use `Parse_DecimalEntries_ParseAsNumbers`.
- `:43-44`: no platform-appropriate path. `:45`: no `example-config.toml`; the examples are in
  `docs/config-examples/`.
- `:116-117`: the registry's registrations are in memory, but runs are stored and reloaded at startup.
- `:119-120`: the "run now" buttons are on the Jobs page.
- `:135`: Follow, Upgrade, Bonus, the lists, the start-up account read and `MamCoverSource` take the gate too.
- `:137-138`: the Account page and `FollowService` take `MamAccessGate` directly.
- `:150-152`: the Seeding job publishes notifications as well.
- `:167-168`: add `Series/`, `Following/`, `Search/`.
- `:170-171`: the pages have no authentication; OPDS has an optional login.
- Project graph: `Clients.Mam` depends on Core.
- Missing altogether: OPDS, `/metrics`, the Backup, Seeding and Library check jobs, bulk operations,
  `LibraryWriteGate`, the data upgrades and the derived `TorrentPeople`/`TorrentSeries` tables.

## Part C: gaps to keep

Found, considered, and better left as they are.

- `development_banner`: undocumented for users on purpose; one line for maintainers (B1) is enough.
- Accepted alternative spellings: PascalCase enum names, `filetypes`, `crude language`, language and category
  short forms. Listing them invites their use.
- Case rules that differ between settings (`[[notify]] format` and events, `[opds]`/`[upgrade] libraries`
  case-sensitive; `client` not). The error message explains itself. Making them all case-insensitive is a
  small code change if it ever bites.
- `classics` working for audio only and `crime`/`thriller` for ebook only: inherited behaviour, harmless.
- Internal surface: `/theme`, the polling handlers, OPDS sub-routes, cookie names, the `--FETCHSHELF_*=`
  argument form, the `FETCHSHELF_VERSION` build argument.
- Limits no one reaches: series numbers above 500, the 10,000-page clamp, 5 MiB covers, the 200-row tag
  preview, the 100 s default timeouts, 12 months on the Account table.
- "About a minute" until `(healthy)` (`user-guide.md:149`): imprecise, harmless.
- Author links dropped for author lists over 40 characters; OPDS *Libraries* needing libraries with books.
- `contributors-guide.md:366` asking for a `config.toml` before the first start, though one is created.
- Labels `dependencies` and `.NET` missing from the labels table; no Dependabot entry for the docs'
  `requirements.txt`.
- `folder_template` refusals not all listed (`configuration.md:1239-1240`); the error text says which.
- Collection books taking their title from the file name when MAM gives no series range
  (`user-guide.md:1634-1635`).

## Part D: decisions needed first

Decided 2026-11-21: D1 keeps the `curl` and drops the "same check" sentence (in #152); D2 and D3 follow the
recommendation (in #153). D4 and D5 are still open.

1. **`checkCookie.php` in the guides** (`user-guide.md:377-380`, `:420`, `getting-started.md:234-242`,
   `:554`). The guides have the user run a `curl` against it, and the user guide adds "This is the same check
   Fetchshelf makes". Fetchshelf never calls it, and it is not among the endpoints MAM's API rules allow to be
   automated. Recommended: drop the "same check" sentence at least; whether a one-off manual `curl` belongs in
   the guides at all is a call on MAM's rules. The Account page's **Refresh from MAM** does the same job
   through an allowed endpoint and could replace it.
2. **`prefer_format` on lists** (`configuration.md:630`, `:700`). The other format is dropped only when both
   turn up in the same search (`ListImporter.cs:422-434`). With the preferred format already owned, the other
   one is selected when it appears later. Recommended: change the code so the preferred format, once selected
   or owned, closes the other side; the doc's "only this format is downloaded" is the natural reading.
3. **`cost = "wedge"` on a torrent that turned free while waiting** (`user-guide.md:1010-1011`,
   `configuration.md:603-604`, "wedges are never wasted"). Only `try_wedge` re-checks at download time
   (`DownloadRunner.cs:248-253`); `wedge` does not, and a `ratio` selection is still held to the ratio budget.
   Recommended: re-check for `wedge` as well, one more search request per wedge download. Otherwise qualify
   both sentences with "when selected".
4. **`--update-search-title`** (`FetchshelfApp.cs:131`): documented nowhere. Data upgrade 1 appears to have
   replaced its purpose. Recommended: remove the mode and `TitleMaintenanceRunner`'s command-line entry if
   nothing else needs them; otherwise a paragraph in the maintainer guide.
5. **A cover of the user's own on relink** (`user-guide.md:1599` against `LibraryFileRemover`): with
   `covers_from_mam = 0`, **Refresh and relink** in place deletes a `cover.jpg` the user put there and does not
   put it back. Recommended: keep the docs change in B2 and leave the code; keeping covers apart from the
   other written files is a larger change than the case deserves.

## How to do it

Docs first, since they are larger and carry no risk; code in small pull requests, each with its doc lines.

1. **Docs, user-facing**: B1 (except developer docs), B2, B3, B4's `database-setup.md` part. One pull request;
   `5-everything.toml` is checked with `--check-config` and the docs site is built once.
2. **Docs, developer-facing**: the developer part of B1 and the secrets lists. `CLAUDE.md` (B5) is edited
   locally alongside.
3. **Config validation**: A4, A5, the `media_type` and section-label items of A10, A9.
4. **Lists**: A1, A8, and D2 if decided.
5. **Linking and torrents**: A2, A3, the `metadata.json` language.
6. **Pages**: A6, A7, the unconfigured-jobs list, the Config page sections, the Library check confirmation.
7. **Logging**: the log-file limits and the `FETCHSHELF_LOG` warning.
8. **`utils/fs`**: A10's item with B4's `fs.md` part.
9. **D1, D3, D4**: as decided.

Steps 3 to 8 are independent of each other. Each ends with build, tests and format check; steps 3 and 7 get a
line under *Upgrading* in `RELEASES.md`, since a config that started before may now be refused (A4, A5) and
log files are now limited.

## Not verified

- Anything in a browser: A7's bare fragment and the position of **Set category** were read from the markup.
- What MAM answers to `checkCookie.php`, and whether MAM deducts a wedge on a torrent that is already free.
- That MAM's description field carries HTML into `metadata.json` and `metadata.opf`
  (`user-guide.md:1511`, `:1563` show plain text); if it does, one clause there says so.
- The Read the Docs project settings in `maintainer-guide.md:259-264`, and that the docs site builds without
  warnings.
- The per-job stop points on Cancel other than Download's (`user-guide.md:2170-2172`), and the Automatic
  Torrent Management remark (`:1960-1964`).
