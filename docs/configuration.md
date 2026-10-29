# Configuration reference

Fetchshelf is configured with a single TOML file, `config.toml`. This page lists every setting, what it
does and how the settings relate to each other. For working configs to start from, see
[`docs/config-examples/`](config-examples/): six numbered examples, from a bare-bones `1-minimal.toml`
to `6-everything.toml`, which uses every option.

## Contents

- [How the file is loaded](#how-the-file-is-loaded)
- [How the pieces fit together](#how-the-pieces-fit-together)
- [Top-level settings](#top-level-settings)
- [`[search]`](#search)
- [`[audiobookshelf]`](#audiobookshelf)
- [`[[qbittorrent]]`](#qbittorrent)
- [The shared torrent filter](#the-shared-torrent-filter)
- [`[[autograb]]`](#autograb)
- [Cost](#cost)
- [`[[snatchlist]]`](#snatchlist)
- [`[[goodreads_list]]`](#goodreads_list)
- [`[[notion_list]]`](#notion_list)
- [`[[tag]]`](#tag)
- [`[[library]]`](#library)
- [Reference data](#reference-data)
- [Complete example](#complete-example)

## How the file is loaded

- **Location.** The path comes from the `FETCHSHELF_CONFIG_FILE` environment variable. If it is not set,
  Fetchshelf uses a platform-appropriate config path, falling back to `config.toml` in the working
  directory. The file must have a `.toml` extension. The database location is set separately with
  `FETCHSHELF_DATA_FILE` (default `data.db`). In Docker, `/config` and `/data` are mounted volumes.
- **Missing file.** If the file does not exist, Fetchshelf creates one containing only an empty `mam_id`
  so the web UI still starts.
- **No `mam_id`.** With an empty `mam_id` the web UI runs but no background job runs.
- **Unknown keys are errors.** A misspelled or unsupported key stops the file from loading, and the error
  names the problem. This applies to every section, including keys inside `grab` blocks.
- **Validation.** Required keys, mutually exclusive keys and unrecognized names (languages, categories,
  flags, media types, costs) are checked at startup. A config that fails validation is rejected as a whole.
- **TOML layout.** Top-level settings must appear before the first `[section]` or `[[section]]` header,
  because anything after a header belongs to that section. Repeatable sections (`[[autograb]]`,
  `[[tag]]`, `[[library]]`, ...) may be interleaved with other sections, and their order is kept.
- **Environment overrides.** Every simple top-level setting can be overridden with an environment variable
  named `FETCHSHELF_CONF_<setting>` with the setting name exactly as written in the file, in lower case,
  for example `FETCHSHELF_CONF_mam_id`. Overrides apply on top of the file. List values are given
  comma-separated: `FETCHSHELF_CONF_ebook_types="epub,pdf"`. Sections (`[[autograb]]`,
  `[[qbittorrent]]`, ...) cannot be overridden this way.
- **Read-only view.** The Config page in the web UI shows the active configuration with secrets hidden.
  Changes to the file take effect after a restart.
- **Logging** is not part of the file: `FETCHSHELF_LOG` sets the level and `FETCHSHELF_LOG_DIR` enables a
  rolling log file in addition to console output.

### Value types used below

| Type | Meaning |
|---|---|
| minutes | A whole number of minutes. `0` means "never on a schedule, run only when started from the UI". |
| size | A number plus a unit: `B`, `KiB`, `MiB`, `GiB`, `TiB` (powers of 1024). `KB`, `MB`, `GB`, `TB` mean the same. Decimals are allowed, for example `"1.2 GiB"`. Written as a string. |
| date | `"YYYY-MM-DD"`, as a string. |
| list | A TOML array of strings, for example `["epub", "pdf"]`. |

## How the pieces fit together

Fetchshelf has two halves that can be used independently.

**Auto-downloader** — finds torrents on MaM and adds them to qBittorrent:

```
[[autograb]]        search MaM on a schedule        ─┐
[[goodreads_list]]  look up books from a shelf       ├─▶ selection ─▶ download ─▶ qBittorrent
[[notion_list]]     look up books from a database   ─┘      ▲
[[snatchlist]]      import what you already own             │ (metadata only, for duplicate detection)
```

- Every source decides **which** torrents qualify using the [shared torrent filter](#the-shared-torrent-filter)
  and **how** to pay for them using [`cost`](#cost).
- `ebook_types` / `audio_types` / ... decide which torrents have a usable file format at all, and which
  of two copies of the same book is better.
- `[[tag]]` rules (or an autograb's own `category`) decide which qBittorrent category and tags a new
  download gets.
- `min_ratio`, `unsat_buffer` and `wedge_buffer` cap how much the downloader may spend. Per-source
  `unsat_buffer` / `wedge_buffer` override the global ones.

**Library organizer** — turns finished downloads into a tidy folder tree:

```
qBittorrent (finished torrent) ─▶ [[library]] match ─▶ Author/Series/Title folder ─▶ Audiobookshelf
          │                         by category or            ▲
          │                         download_dir              │ path_mapping translates the
          └─ category/tags set by [[tag]] rules ─────────────┘ client's paths to Fetchshelf's
```

- A `[[library]]` picks up torrents by their **qBittorrent category** (which a `[[tag]]` rule or an
  autograb `category` sets) or by their **save path** (`download_dir`).
- `[[qbittorrent]]` `path_mapping` tells Fetchshelf where the client's files are from its own point of
  view, which matters in Docker.
- `[audiobookshelf]` is optional; without it Audiobookshelf can still pick the books up from the
  `metadata.json` Fetchshelf writes next to the files.
- When a better format of a book is linked, the old copy is retired, and `on_cleaned` on the
  `[[qbittorrent]]` entry says what to do to it in the client.

## Top-level settings

These go at the top of the file, before any section.

| Setting | Default | Description |
|---|---|---|
| `mam_id` | (required) | MaM session id: the cookie value created under your MaM security preferences. Empty means the UI runs but no jobs do. Treat it as a secret. |
| `web_host` | `"0.0.0.0"` | Address the web UI listens on. |
| `web_port` | `3157` | Port the web UI listens on. |
| `min_ratio` | `2` | Fetchshelf never starts a download that would take your account below this ratio. |
| `unsat_buffer` | `10` | Number of unsatisfied-snatch slots always left free. Can be overridden per autograb and per list. |
| `wedge_buffer` | `0` | Number of freeleech wedges always left unused. Can be overridden per autograb and per list. |
| `add_torrents_stopped` | `false` | Add torrents to qBittorrent in the stopped state. |
| `exclude_narrator_in_library_dir` | `false` | Leave the `{Narrator}` part out of library folder names. See [folder naming](#folder-naming). |
| `search_interval` | `30` | Minutes between runs of each autograb and snatchlist block. Overridable per block. |
| `link_interval` | `10` | Minutes between scans for finished torrents to link into libraries. |
| `import_interval` | `135` | Minutes between runs of each Goodreads and Notion list. Overridable per list. Also accepted as `goodreads_interval`. |
| `ignore_torrents` | `[]` | MaM torrent ids that must never be selected, as strings. |
| `audio_types` | `["m4b", "m4a", "mp4", "mp3", "ogg"]` | File extensions wanted for Audiobooks and Periodical Audiobooks, best first. |
| `ebook_types` | `["cbz", "epub", "pdf", "mobi", "azw3", "azw", "cbr"]` | Same for Ebooks, Manga, Comic Books and Periodical Ebooks. |
| `music_types` | `["flac", "mp3", "pdf"]` | Same for Musicology. |
| `radio_types` | `["mp3"]` | Same for Radio. |
| `page_explanations` | `true` | Show a short explanation under each page title in the web UI. |
| `job_explanations` | `true` | Show a short description of each job on the Jobs page. |

Notes:

- **Format lists do three jobs.** A torrent is only downloaded if it contains at least one listed
  extension, only listed extensions are linked into a library, and the list order ranks competing copies
  of the same book: the copy whose first listed extension comes earliest wins. With
  `ebook_types = ["epub", "pdf"]`, an epub replaces a pdf of the same book. A `[[library]]` can override
  `audio_types` and `ebook_types` for linking.
- **Intervals** are per job: each `[[autograb]]` and `[[snatchlist]]` block runs on its own timer, and so
  does each list. A job with interval `0` is still listed on the Jobs page and can be started with
  **Run now**.
- **Buffers** are subtracted from your limits before anything is downloaded. If your unsat limit is 200,
  you have 150 unsatisfied snatches and `unsat_buffer` is 10, a run may add at most 40 torrents.
- **Ratio budget.** Free torrents cost no ratio. Others may only be downloaded while the remaining
  budget, `(uploaded − downloaded − bytes already downloading) / min_ratio`, covers their size.

## `[search]`

Optional. Settings for the web UI's manual Search page.

| Setting | Default | Description |
|---|---|---|
| `wedge_over` | unset | A [size](#value-types-used-below). Search results larger than this get the "download with wedge" button highlighted. |

## `[audiobookshelf]`

Optional. Leave the section out to turn the integration off.

| Setting | Default | Description |
|---|---|---|
| `url` | (required) | Base URL of your Audiobookshelf server. |
| `token` | (required) | API token for Audiobookshelf. Treat it as a secret. |
| `interval` | `10` | Minutes between runs of the matcher. |

What it does:

- The **matcher** looks for linked torrents that have no Audiobookshelf id yet, finds the Audiobookshelf
  library whose folders contain the book's library path, and stores the matching item's id.
- After linking, and after a metadata refresh, updated metadata is pushed to matched items.
- When a lesser format is retired, the old Audiobookshelf item is deleted.
- The torrent pages link to the matched Audiobookshelf item.

## `[[qbittorrent]]`

Repeatable. Each entry is one qBittorrent Web UI. New downloads go to the **first** entry. Linking runs
against every entry.

| Setting | Default | Description |
|---|---|---|
| `url` | (required) | Web UI URL, for example `http://localhost:8080`. |
| `username` | (required) | Web UI user. |
| `password` | (required) | Web UI password. Treat it as a secret. |
| `path_mapping` | `{}` | Table mapping a path prefix as qBittorrent sees it to the same location as Fetchshelf sees it. |
| `on_cleaned` | unset | What to do in qBittorrent to a torrent whose files were retired in favour of a better format. A table with an optional `category` and a `tags` list. |
| `on_invalid_torrent` | unset | The same table shape. It is accepted and validated, but nothing acts on it yet. |

```toml
[[qbittorrent]]
url = "http://qbittorrent:8080"
username = "admin"
password = "replace-with-your-qbittorrent-admin-password"

[qbittorrent.path_mapping]
"/downloads" = "/data/torrents"      # qBittorrent's path = Fetchshelf's path

[qbittorrent.on_cleaned]
category = "superseded"
tags = ["superseded"]
```

- **`path_mapping`** is needed when Fetchshelf and qBittorrent see the same folders under different
  paths, which is typical in Docker. The longest matching prefix wins, and it replaces just that prefix.
  Without a mapping, the paths qBittorrent reports are used as they are.
- **Categories** named in `on_cleaned`, `[[tag]]` and `[[autograb]]` are created in qBittorrent
  automatically if they do not exist.
- Hardlinks only work when the download folder and the library folder are on the same filesystem. See
  [`method`](#library).

## The shared torrent filter

`[[autograb]]`, `[[snatchlist]]`, `[[tag]]` and every `grab` block of a Goodreads or Notion list share one
set of optional filter fields. A torrent matches only if it satisfies **every** field that is set. A field
that is not set imposes no restriction (except `categories`, which always has defaults).

| Field | Description |
|---|---|
| `name` | A label for the block, shown in logs, events and the UI. Needed for `max_active_downloads` on an autograb. |
| `media_type` | List of media types. The torrent must be one of them. See [media types](#media-types). |
| `categories` | Per main type: `true` (all categories), `false` (none, so that type is excluded) or a list of category names. Keys: `audio`, `ebook`, `musicology`, `radio`. Defaults: `audio` and `ebook` are `true`, `musicology` and `radio` are `false`. See [categories](#categories). |
| `languages` | List of language names. The torrent's language must be in the list; a torrent whose language is unknown fails. See [languages](#languages). |
| `flags` | Table of flag name to `true` (flag required) or `false` (flag forbidden). Flags you do not mention are ignored. See [flags](#flags). |
| `min_size`, `max_size` | [Sizes](#value-types-used-below), inclusive. |
| `exclude_uploader` | List of uploader names to reject. |
| `uploaded_after`, `uploaded_before` | [Dates](#value-types-used-below). Inclusive of the whole day. |
| `min_seeders`, `max_seeders` | Inclusive numeric bounds. |
| `min_leechers`, `max_leechers` | Inclusive numeric bounds. |
| `min_snatched`, `max_snatched` | Inclusive bounds on the number of times the torrent was completed. |
| `max_authors` | Maximum number of authors the torrent may list, inclusive. Use it to skip anthologies. |

```toml
media_type = ["Ebook"]
categories = { ebook = ["Fantasy", "Science Fiction"] }
languages = ["English"]
flags = { explicit = false, abridged = false }
max_size = "50 MiB"
max_authors = 3
```

How the filter is applied:

- Where possible the filter is translated into MaM search parameters so MaM narrows the results, and the
  **full filter is then applied again locally** to every result. The local check is what counts.
- Because `categories` defaults to "audio and ebook, nothing else", a block without `categories` never
  matches Musicology or Radio. To include them, set `categories = { musicology = true }` or the
  `radio` equivalent.
- `categories` also decides what a list can look for. A list whose `grab` blocks all exclude ebooks never
  wants ebooks.
- Several `flags` may be set at once, so one block can both require and forbid flags.
- A `[[tag]]` rule can also be applied to torrents you already have, from the Config page. In that case
  fields that need live data (uploader, seeders, leechers, snatched counts) are looked up from MaM
  for each torrent.

## `[[autograb]]`

Repeatable. An autograb is a scheduled MaM search. Each run searches, filters the results, selects what
qualifies within your budget, and hands the selection to the downloader.

Takes all [shared filter](#the-shared-torrent-filter) fields, plus:

| Setting | Default | Description |
|---|---|---|
| `type` | (required) | What to search. See below. |
| `cost` | `"free"` | How to pay for matching torrents. See [cost](#cost). |
| `query` | unset | Free-text search. Supports MaM's search syntax. |
| `search_in` | `[]` | Which fields the query matches against: `author`, `description`, `filenames`, `file_types`, `narrator`, `series`, `tags`, `title`. Empty means MaM's default fields. |
| `sort_by` | newest first | `newest_first` (same as unset), `oldest_first`, `random`, `low_seeders` or `low_snatches`. |
| `max_pages` | 50 for `bookmarks`, `freeleech`, `mine`; 1 otherwise | Number of result pages to read per run. A page is 100 results (fixed). Paging stops early when a page comes back short. |
| `search_interval` | global `search_interval` | Minutes between runs. `0` means UI-triggered only. |
| `unsat_buffer` | global | Overrides the global buffer for this block. |
| `wedge_buffer` | global | Overrides the global buffer for this block. |
| `max_active_downloads` | unset | Cap on torrents selected by this block that have not finished downloading. Requires `name`. |
| `dry_run` | `false` | Search and run every selection check, but select, write and download nothing. What each torrent would get is logged. |
| `mark_removed` | `false` | For torrents Fetchshelf already stores: if the results show a gap where a stored torrent should be, ask MaM directly, and mark the torrent as removed from MaM if it is gone. |
| `category` | unset | qBittorrent category forced on everything this block downloads. Overrides `[[tag]]` rules. |

### `type`

| Value | Searches |
|---|---|
| `"bookmarks"` | Your MaM bookmarks. |
| `"freeleech"` | The global freeleech list. |
| `"new"` (alias `"torrents"`) | **All torrents** on MaM. The name refers to the default newest-first ordering, not to a separate set of torrents. |
| `"mine"` | Your own uploads. |
| `{ uploader = 12345 }` | One uploader's torrents, by MaM user id. |

`type` is required and has no default. `type` chooses the pool of torrents; `sort_by` chooses their order
within it.

### Relationships

- **Paging.** `max_pages` × 100 is the most results one run will read. For `new` with newest-first
  ordering and one page, a run only sees the 100 newest matches, which is enough for a block that runs
  every few minutes but will not backfill old books. Raise `max_pages` or use
  `uploaded_after` to control the window.
- **Budget.** A run may select at most `unsat limit − unsat count − unsat_buffer` torrents, further capped
  by `max_active_downloads` if set. With a budget of 0, nothing is selected (unless `cost` is
  `metadata_only` or `metadata_only_add`).
- **Duplicates.** A candidate that is the same book as something already selected or already in the
  library is skipped unless its format ranks better; a better format is selected and the older copy is
  retired by the cleaner later.
- **Several blocks.** Blocks run on their own timers, and only one MaM-talking job runs at a time.
  A book that one block has already selected is not selected again by another.
- **Never on a schedule.** `search_interval = 0` makes a block run only when you press **Run now**.

```toml
[[autograb]]
name = "New Terry Pratchett ebooks"
type = "new"
cost = "all"
query = '"Terry Pratchett"'
search_in = ["author"]
media_type = ["Ebook"]
languages = ["English"]
max_authors = 3
max_size = "30 MiB"
```

## Cost

`cost` appears on `[[autograb]]`, `[[snatchlist]]` and each `grab` block of a list.

| Value | Meaning |
|---|---|
| `free` (default) | Only torrents that are free: VIP, personal freeleech or global freeleech. Others are not selected. |
| `wedge` | Use a freeleech wedge on torrents that are not already free. The download fails if wedges are at or below `wedge_buffer`. |
| `try_wedge` | Use a wedge if one is available above `wedge_buffer`, otherwise download without one and pay with ratio. |
| `ratio` (alias `all`) | Pay with ratio. Free torrents are still free. |
| `metadata_only` | Record the torrent's metadata for books you already have, without downloading. |
| `metadata_only_add` | Like `metadata_only`, but also creates a metadata-only entry if the torrent is not in the library yet. |

- A torrent that is VIP, personal freeleech or globally freeleech is **always** treated as free, whatever
  `cost` says, so wedges are never wasted.
- For `wedge` and `try_wedge` the wedge is applied at download time, not when the torrent is selected.
  `try_wedge` also skips the wedge when the torrent is already free.
- `metadata_only` and `metadata_only_add` never download anything, so ratio and unsat budgets do not apply
  to them.

## `[[snatchlist]]`

Repeatable. Imports your MaM snatch lists as metadata-only entries so that torrents you already have count
as owned when Fetchshelf checks for duplicates. Nothing is downloaded.

Takes the [shared filter](#the-shared-torrent-filter), except that **`languages`, `uploaded_after` and
`uploaded_before` are not allowed** and cause an error.

| Setting | Default | Description |
|---|---|---|
| `type` | `unsat` | Which snatch list: `unsat`, `inact_unsat`, `seed_unsat`, `seed_sat`, `inact_sat` or `uploads_active`. |
| `cost` | `metadata_only` | Only `metadata_only` and `metadata_only_add` are allowed. |
| `max_pages` | 50 | Pages of 250 rows to read. |
| `search_interval` | global `search_interval` | Minutes between runs. |
| `dry_run` | `false` | Read the snatch list and look each torrent up, but record nothing. What would be recorded is logged. |

## `[[goodreads_list]]`

Repeatable. Reads a Goodreads shelf and looks up each book on MaM.

| Setting | Default | Description |
|---|---|---|
| `url` | (required) | The shelf's RSS feed URL. |
| `name` | unset | Label for the list. |
| `prefer_format` | unset | `"audio"` or `"ebook"`. When both formats of a book are found, the other one is marked not wanted. |
| `grab` | (at least one required) | One or more [`grab` blocks](#grab-blocks). |
| `search_interval` | `import_interval` | Minutes between runs. |
| `unsat_buffer` | global | Overrides the global buffer for this list. |
| `wedge_buffer` | global | Overrides the global buffer for this list. |
| `dry_run` | `false` | Log only. |

The list's identity is the Goodreads user id plus the shelf name, taken from the URL.

How a run works, for each book on the shelf:

1. Books that already have both formats found, or their preferred format found, are skipped.
2. Your library is checked with fuzzy title and author matching. Formats you own count as existing.
3. For each `grab` block in order, MaM is searched for the title and author, and that block's filter is
   applied. For each format (audio, ebook), the first block that finds a hit wins.
4. The best hit per format is chosen. If `prefer_format` is set and both formats were found, the other
   one is dropped.
5. The result is selected using the winning block's `cost`.

Series information is read from titles such as `Title (Series, #3)`.

## `[[notion_list]]`

Repeatable. Reads rows from a Notion data source and fetches the MaM torrents they point to.

| Setting | Default | Description |
|---|---|---|
| `data_source` | (required) | The Notion data source id. |
| `token` | (required) | Notion integration token. Treat it as a secret. |
| `name` | (required) | Label for the list. |
| `mam_fields` | (required, non-empty) | Names of the row properties that hold MaM URLs. The trailing number of each URL is the MaM id. |
| `grab` | (at least one required) | One or more [`grab` blocks](#grab-blocks). |
| `search_interval` | `import_interval` | Minutes between runs. |
| `unsat_buffer` | global | Overrides the global buffer. |
| `wedge_buffer` | global | Overrides the global buffer. |
| `dry_run` | `false` | Log only. |

MaM ids that are already known are skipped. The others are fetched from MaM and passed through the first
matching `grab` block.

### `grab` blocks

Used inside `[[goodreads_list]]` and `[[notion_list]]` as `[[goodreads_list.grab]]` and
`[[notion_list.grab]]`.

A `grab` block is a [`cost`](#cost) plus the full [shared filter](#the-shared-torrent-filter). Blocks are
tried in order and the **first matching block wins**, so put the more specific block first:

```toml
[[goodreads_list.grab]]
cost = "all"             # small files: pay with ratio
languages = ["English"]
max_size = "15 MiB"

[[goodreads_list.grab]]
cost = "wedge"           # everything else English: use a wedge
languages = ["English"]
```

## `[[tag]]`

Repeatable. A tag rule is the [shared filter](#the-shared-torrent-filter) plus what to do in qBittorrent
with matching downloads.

| Setting | Default | Description |
|---|---|---|
| `category` | unset | qBittorrent category to assign. |
| `tags` | `[]` | qBittorrent tags to add. |

For each new download:

- The **category** comes from the **first** matching rule that sets one.
- The **tags** are the **union** of the tags from **all** matching rules.
- An autograb's own `category` overrides the rules' category.

The result is applied when the torrent is added to qBittorrent. Each rule can also be applied to torrents
you already have, from the Config page.

Rules are what connect downloads to libraries: a `[[library]]` with `category = "ebooks"` picks up
torrents that a `[[tag]]` rule labelled `ebooks`.

```toml
[[tag]]
categories = { audio = false, ebook = true }
category = "ebooks"
tags = ["fetchshelf"]
```

## `[[library]]`

Repeatable. A library maps finished torrents to an output folder. For each finished torrent the **first**
library, in file order, that accepts it is used.

| Setting | Default | Description |
|---|---|---|
| `library_dir` | (required) | Root folder the books are linked into. |
| `category` | | Accept torrents with this qBittorrent category. |
| `download_dir` | | Accept torrents whose save path is under this folder. |
| `name` | unset | Label for the library. |
| `method` | `"hardlink"` | How files are placed. See below. |
| `allow_tags` | `[]` | If not empty, the torrent must have at least one of these tags. |
| `deny_tags` | `[]` | A torrent with any of these tags is rejected. |
| `audio_types` | global | Overrides the global format list for linking into this library. |
| `ebook_types` | global | Overrides the global format list for linking into this library. |

- **Exactly one of `category` or `download_dir`** must be set. Setting both or neither is an error.
- **Tags.** A torrent with any `deny_tags` tag is rejected. If `allow_tags` is not empty, the torrent
  must also have at least one of them.
- **`method`:**

| Value | Behaviour |
|---|---|
| `hardlink` | Hard link; fails if source and library are on different filesystems. |
| `hardlink_or_copy` | Hard link, falling back to a copy. |
| `hardlink_or_symlink` | Hard link, falling back to a symbolic link. |
| `copy` | Copy the files. |
| `symlink` | Symbolic link. |
| `no_link` | Record metadata only; no files are placed. |

- Only files with a wanted extension are linked, and the library's own `audio_types` / `ebook_types`
  decide what that means for it.
- Files in subfolders named like `CD 1`, `Disc 2` or `Disk 3` go to a `Disc N` folder. Other folder
  structure is flattened.
- A `metadata.json` that Audiobookshelf understands is written next to the files.
- A torrent without any author cannot be linked and is reported on the Errors page.

### Folder naming

Under `library_dir`, each part is sanitized into a valid file name:

- With series: `Author/Series/Series #<entries> - Title`.
- Without series: `Author/Title`.
- The author is the first author.
- An edition adds `, <Edition>` to the last part.
- Narrators add ` {<first narrator>}` to the last part, unless `exclude_narrator_in_library_dir` is `true`.
  With that set, if the narrator-less folder already exists on disk, the narrator form is used to avoid a
  collision.

Examples:

```
Brandon Sanderson/Stormlight Archive/Stormlight Archive #1 - The Way of Kings {Michael Kramer}
Andy Weir/Project Hail Mary {Ray Porter}
Terry Pratchett/Discworld/Discworld #1 - The Colour of Magic
```

When metadata or config changes so a linked book's expected folder differs from where it is, the UI flags
it as a mismatch (no library, new library directory, or new path) and offers **refresh and relink** to
move the files. Changing `exclude_narrator_in_library_dir` alone does not count as a mismatch.

## Reference data

### Media types

Used by `media_type`. Names are case-insensitive.

`Audiobook`, `Ebook`, `Musicology`, `Radio`, `Manga`, `ComicBook`, `PeriodicalEbook`,
`PeriodicalAudiobook`.

For `categories` and the format lists these group into four main types: **audio** (Audiobook, Periodical
Audiobook), **ebook** (Ebook, Manga, Comic Book, Periodical Ebook), **musicology** and **radio**.

### Categories

Used inside `categories`. Names are case-insensitive, and obvious simplified spellings are accepted.

- **Audio:** Action/Adventure, Art, Biographical, Business, Computer/Internet, Crafts, Crime/Thriller,
  Fantasy, Food, General Fiction, General Non-fic, Historical Fiction, History, Home/Garden, Horror,
  Humor, Instructional, Juvenile, Language, Literary Classics, Math/Science/Tech, Medical, Mystery,
  Nature, Philosophy, Pol/Soc/Relig, Recreation, Romance, Science Fiction, Self-Help, Travel/Adventure,
  True Crime, Urban Fantasy, Western, Young Adult.
- **Ebook:** the same names plus Comics/Graphic Novels, Illusion/Magic, Magazines/Newspapers and
  Mixed Collections.
- **Musicology:** Guitar/Bass Tabs, Individual Sheet, Individual Sheet MP3, Instructional Book with
  Video, Instructional Media - Music, Lick Library - LTP/Jam With, Lick Library - Techniques/QL,
  Music - Complete Editions, Music Book, Music Book MP3, Sheet Collection, Sheet Collection MP3.
- **Radio:** Comedy, Drama, Factual/Documentary, Reading.

An unrecognized category name is a configuration error.

### Flags

Used inside `flags`. Names are case-insensitive.

| Flag | Accepted names |
|---|---|
| Crude language | `crude_language`, `crude`, `language` |
| Violence | `violence` |
| Some explicit | `some_explicit` |
| Explicit | `explicit` |
| Abridged | `abridged` |
| LGBT | `lgbt` |

### Languages

Used by `languages`. Names are case-insensitive and common aliases work (for example `Brazilian`,
`Castilian`, `Scottish`).

English, Chinese, Gujarati, Spanish, Kannada, Burmese, Thai, Hindi, Marathi, Telugu, Tamil, Javanese,
Vietnamese, Punjabi, Urdu, Russian, Afrikaans, Bulgarian, Catalan, Czech, Danish, Dutch, Finnish,
Scottish Gaelic, Ukrainian, Greek, Hebrew, Hungarian, Tagalog, Romanian, Serbian, Arabic, Malay,
Portuguese, Bengali, French, German, Japanese, Farsi, Swedish, Korean, Turkish, Italian, Cantonese,
Polish, Latin, Other, Norwegian, Croatian, Lithuanian, Bosnian, Brazilian Portuguese, Indonesian,
Slovenian, Castilian Spanish, Irish, Manx, Malayalam, Ancient Greek, Sanskrit, Estonian, Latvian,
Icelandic.

## Complete example

```toml
mam_id = "your-mam-session-id"
min_ratio = 2
unsat_buffer = 10
wedge_buffer = 5
search_interval = 15
link_interval = 10
ebook_types = ["epub", "pdf", "mobi"]
audio_types = ["m4b", "mp3"]

[search]
wedge_over = "100 MiB"

[audiobookshelf]
url = "http://audiobookshelf:13378"
token = "abs-api-token"

[[qbittorrent]]
url = "http://qbittorrent:8080"
username = "admin"
password = "replace-with-your-qbittorrent-admin-password"

[qbittorrent.path_mapping]
"/downloads" = "/data/torrents"

[qbittorrent.on_cleaned]
tags = ["superseded"]

# Free bookmarked books, then small ones with ratio, then wedges for the rest.
[[autograb]]
name = "Free bookmarks"
type = "bookmarks"
cost = "free"

[[autograb]]
name = "Small bookmarks"
type = "bookmarks"
cost = "all"
max_size = "30 MiB"

[[autograb]]
name = "Other bookmarks"
type = "bookmarks"
cost = "try_wedge"

# Watch for new Terry Pratchett ebooks, but not anthologies.
[[autograb]]
name = "New Terry Pratchett ebooks"
type = "new"
cost = "all"
query = '"Terry Pratchett"'
search_in = ["author"]
media_type = ["Ebook"]
languages = ["English"]
max_authors = 3

# Classify downloads so libraries can pick them up.
[[tag]]
categories = { audio = false, ebook = true }
category = "ebooks"

[[tag]]
categories = { audio = true, ebook = false }
category = "audiobooks"

[[library]]
name = "Ebooks"
category = "ebooks"
library_dir = "/data/library/ebooks"

[[library]]
name = "Audiobooks"
category = "audiobooks"
library_dir = "/data/library/audiobooks"
