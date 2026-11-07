# Configuration reference

Fetchshelf is configured with a single TOML file, `config.toml`. This page lists every setting, what it
does and how the settings relate to each other. For working configs to start from, see
[`docs/config-examples/`](config-examples/): five numbered examples, from a bare-bones `1-minimal.toml`
to `5-everything.toml`, which uses every option.

## Contents

- [How the file is loaded](#how-the-file-is-loaded)
- [How the pieces fit together](#how-the-pieces-fit-together)
- [Top-level settings](#top-level-settings)
- [`[search]`](#search)
- [`[audiobookshelf]`](#audiobookshelf)
- [`[hardcover]`](#hardcover)
- [`[[qbittorrent]]`](#qbittorrent)
- [The shared torrent filter](#the-shared-torrent-filter)
- [`[[autograb]]`](#autograb)
- [Cost](#cost)
- [`[[snatchlist]]`](#snatchlist)
- [`[[hardcover_list]]`](#hardcover_list)
- [`[[tag]]`](#tag)
- [`[[library]]`](#library)
- [Reference data](#reference-data)
- [Complete example](#complete-example)

## How the file is loaded

- **Location.** The path comes from the `FETCHSHELF_CONFIG_FILE` environment variable. If it is not set,
  Fetchshelf uses a platform-appropriate config path, falling back to `config.toml` in the working
  directory. The file must have a `.toml` extension. The database location is set separately with
  `FETCHSHELF_DATA_FILE` (default `data.db`). In Docker, `/config` and `/database` are mounted volumes.
  Keep the database on a disk local to the machine Fetchshelf runs on, which includes a NAS running
  Fetchshelf in its own Docker. With [`database_wal`](#top-level-settings) on (the default), SQLite keeps
  two working files next to `data.db`: `data.db-wal` and `data.db-shm`. Leave them in place, and stop
  Fetchshelf before copying the database as a backup. If the database has to be on a network share (NFS or
  SMB), set `database_wal = false`: the write-ahead log can't work over one. Even then, SQLite can't lock
  files reliably across a network, so a local disk is safer.
- **Missing file.** If the file does not exist, Fetchshelf creates one containing only an empty `mam_id`
  so the web UI still starts.
- **No `mam_id`.** With an empty `mam_id`, and no `mam_id_file`, the web UI runs but no background job runs.
- **Unknown keys are errors.** A misspelled or unsupported key stops the file from loading, and the error
  names the problem. This applies to every section.
- **Validation.** Required keys, mutually exclusive keys and unrecognized names (languages, categories,
  flags, media types, costs, `sort_by` values, `search_in` fields) are checked at startup. A config that
  fails validation is rejected as a whole.
- **TOML layout.** Top-level settings must appear before the first `[section]` or `[[section]]` header,
  because anything after a header belongs to that section. Repeatable sections (`[[autograb]]`,
  `[[tag]]`, `[[library]]`, ...) may be interleaved with other sections, and their order is kept.
- **Environment overrides.** Every simple top-level setting can be overridden with an environment variable
  named `FETCHSHELF_CONF_<setting>` with the setting name exactly as written in the file, in lower case,
  for example `FETCHSHELF_CONF_mam_id`. Overrides apply on top of the file. List values are given
  comma-separated: `FETCHSHELF_CONF_ebook_types="epub,pdf"`. Sections (`[[autograb]]`,
  `[[qbittorrent]]`, ...) cannot be overridden this way, with one exception: the Hardcover token, as
  `FETCHSHELF_CONF_hardcover_token` or `FETCHSHELF_CONF_hardcover_token_file` (see [`[hardcover]`](#hardcover)).
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

**Auto-downloader** — finds torrents on MAM and adds them to qBittorrent:

```
[[autograb]]        search MAM on a schedule        ───▶ selection ─▶ download ─▶ qBittorrent
                                                            ▲
[[snatchlist]]      import what you already own             │ (metadata only, for duplicate detection)
                                                            │
[[hardcover_list]]  books you want, from Hardcover ─────────┘
```

- Every source decides **which** torrents qualify using the [shared torrent filter](#the-shared-torrent-filter)
  and **how** to pay for them using [`cost`](#cost).
- `ebook_types` / `audio_types` / ... decide which torrents have a usable file format at all, and which
  of two copies of the same book is better.
- `[[tag]]` rules (or an autograb's own `category`) decide which qBittorrent category and tags a new
  download gets.
- `min_ratio`, `unsat_buffer` and `wedge_buffer` cap how much the downloader may spend. An autograb's own
  `unsat_buffer` / `wedge_buffer` override the global ones. The downloader reads them when it starts each
  torrent, so a changed value also applies to torrents already waiting on the **Selected** page.

**Library organizer** — turns finished downloads into a tidy folder tree:

```
qBittorrent (finished torrent) ─▶ [[library]] match ─▶ Author/Series/Title folder + metadata.json
          │                         by category or            ▲
          │                         download_dir              │ path_mapping translates the
          └─ category/tags set by [[tag]] rules ─────────────┘ client's paths to Fetchshelf's
```

- A `[[library]]` picks up torrents by their **qBittorrent category** (which a `[[tag]]` rule or an
  autograb `category` sets) or by their **save path** (`download_dir`).
- `[[qbittorrent]]` `path_mapping` tells Fetchshelf where the client's files are from its own point of
  view, which matters in Docker.
- Every library, whatever its media types, gets the same folder tree and a `metadata.json` next to each
  book's files, readable by any app or script.
- `[audiobookshelf]` is optional; without it Audiobookshelf can still pick the books up from the
  `metadata.json` files.
- When a better format of a book is linked, the old copy is retired, and `on_cleaned` on the
  `[[qbittorrent]]` entry says what to do to it in the client.

## Top-level settings

These go at the top of the file, before any section.

| Setting | Default | Description |
|---|---|---|
| `mam_id` | (required, or `mam_id_file`) | MAM session id: the cookie value created under your MAM security preferences. Empty means the UI runs but no jobs do. Treat it as a secret. |
| `mam_id_file` | unset | Full path of a file that holds the MAM session id, instead of writing it in `mam_id`. See [below](#reading-mam_id-from-a-file). |
| `web_host` | `"0.0.0.0"` | Address the web UI listens on. |
| `web_port` | `3157` | Port the web UI listens on. |
| `min_ratio` | `2` | Fetchshelf never starts a download that would take your account below this ratio. |
| `unsat_buffer` | `10` | Number of unsatisfied-snatch slots always left free. Can be overridden per autograb. |
| `wedge_buffer` | `0` | Fetchshelf never uses a freeleech wedge that would leave fewer than this many, including for **Wedge download** on the Search page. Can be overridden per autograb. |
| `add_torrents_stopped` | `false` | Add torrents to qBittorrent in the stopped state. |
| `download_after_selection` | `true` | Start the Download job as soon as an autograb, a Hardcover list or the Search page selects a torrent. With `false`, selections wait on the **Selected** page for the next scheduled Download run, every `search_interval` minutes, which leaves time to look them over first. |
| `exclude_narrator_in_library_dir` | `false` | Leave the `{Narrator}` part out of library folder names. See [folder naming](#folder-naming). |
| `database_wal` | `true` | Use SQLite's write-ahead log, so pages stay responsive while a job writes. Set `false` if the database is on a network share. Read at startup; turning it off switches an existing database back. See [How the file is loaded](#how-the-file-is-loaded). |
| `link_method` | `"hardlink_or_copy"` | How files are placed in every `[[library]]` that does not set its own `method`. Same values as [`method`](#library). |
| `search_interval` | `30` | Minutes between runs of each autograb and snatchlist block. Overridable per block. |
| `link_interval` | `10` | Minutes between scans for finished torrents to link into libraries. |
| `ignore_torrents` | `[]` | MAM torrent ids that must never be selected, as strings. |
| `audio_types` | `["m4b", "m4a", "mp4", "mp3", "ogg"]` | File extensions wanted for Audiobooks and Periodical Audiobooks, best first. |
| `ebook_types` | `["cbz", "epub", "pdf", "mobi", "azw3", "azw", "cbr"]` | Same for Ebooks, Manga, Comic Books and Periodical Ebooks. |
| `music_types` | `["flac", "mp3", "pdf"]` | Same for Musicology. |
| `radio_types` | `["mp3"]` | Same for Radio. |
| `page_explanations` | `true` | Show a short explanation under each page title in the web UI. |
| `job_explanations` | `true` | Show a short description of each job on the Jobs page. |

Notes:

- **Format lists do three jobs.** A torrent is only downloaded if it contains at least one listed
  extension, only its best listed formats are linked into a library (see [`[[library]]`](#library)), and
  the list order ranks competing copies of the same book: the copy whose first listed extension comes
  earliest wins, and between two linked copies that rank the same, the larger one is kept. With
  `ebook_types = ["epub", "pdf"]`, an epub replaces a pdf of the same book. A `[[library]]` can override
  `audio_types` and `ebook_types` for linking.
- **Intervals** are per job: each `[[autograb]]` and `[[snatchlist]]` block runs on its own timer. A job
  with interval `0` is still listed on the Jobs page and can be started with **Run now**.
- **Buffers** are subtracted from your limits before anything is downloaded. If your unsat limit is 200,
  you have 150 unsatisfied snatches and `unsat_buffer` is 10, a run may add at most 40 torrents.
- **Ratio budget.** Free torrents cost no ratio. Others may only be downloaded while the remaining
  budget, `(uploaded − downloaded − bytes already downloading) / min_ratio`, covers their size.

### Reading `mam_id` from a file

`mam_id_file` keeps the session id out of `config.toml`, for example in a Docker secret:

```toml
mam_id_file = "/run/secrets/mam_id"
```

- **One or the other.** Setting both `mam_id` and `mam_id_file` is an error. An empty `mam_id = ""`, as in
  the file Fetchshelf creates on first start, does not count as set.
- **Full path.** The value must be a full path. Fetchshelf reads the file at startup, so a changed session
  id takes effect after a restart.
- **Contents.** The file holds only the session id. Spaces and line breaks around it are ignored, so a
  trailing newline is fine. An empty file is the same as an empty `mam_id`: the web UI runs but no job
  does.
- **Errors.** A file that does not exist or cannot be read stops Fetchshelf from starting, and the error
  names the path.
- **Environment.** Like `mam_id`, it can be set with `FETCHSHELF_CONF_mam_id_file`. The check for both
  being set happens after environment overrides, wherever each value comes from.
- **Config page.** The Config page shows the path; the session id itself stays hidden.

## `[search]`

Optional. Settings for the web UI's manual Search page.

| Setting | Default | Description |
|---|---|---|
| `wedge_over` | unset | A [size](#value-types-used-below). Search results larger than this get the "download with wedge" button highlighted. |

## `[audiobookshelf]`

Optional. Leave the section out to turn the integration off.

| Setting | Default | Description |
|---|---|---|
| `url` | (required) | Base URL of your Audiobookshelf server. Also used for the links to Audiobookshelf on torrent pages, so use the address you open it at in a browser, such as `http://192.168.1.10:13378`, rather than a Docker service name. |
| `token` | (required) | API token for Audiobookshelf. Treat it as a secret. |
| `interval` | `10` | Minutes between runs of the **Audiobookshelf** job, which matches linked books to Audiobookshelf items. |

What it does:

- The **matcher** looks for linked torrents that have no Audiobookshelf id yet, finds the Audiobookshelf
  library whose folders contain the book's library path, and stores the matching item's id. Audiobookshelf
  must therefore see the library at the same path as Fetchshelf.
- After linking, and after a metadata refresh, updated metadata is pushed to matched items.
- When a lesser format is retired into another folder than the better copy's, and when a book is unlinked,
  its Audiobookshelf item is deleted.
- The torrent pages link to the matched Audiobookshelf item.

## `[hardcover]`

Optional. The token for [Hardcover](https://hardcover.app)'s API, which the
[`[[hardcover_list]]`](#hardcover_list) entries need. Leave the section out if you don't use Hardcover.

| Setting | Default | Description |
|---|---|---|
| `token` | (required, or `token_file`) | A personal access token from Hardcover: **Settings → Hardcover API → New API Key**. It may start with `Bearer `. Treat it as a secret. |
| `token_file` | unset | Full path of a file that holds the token, instead of writing it in `token`. Works like [`mam_id_file`](#reading-mam_id-from-a-file). |

```toml
[hardcover]
token_file = "/run/secrets/hardcover_token"
```

- **Permissions.** When you create the token, Hardcover asks which permissions it gets. Fetchshelf only
  reads, and needs `read:me` (to know which user you are), `read:library` (your reading statuses) and, for
  `list`, `read:lists`. Without one of them the job fails with a message naming the missing permission.
- **Expiry.** Hardcover tokens expire on the date you chose. After that the jobs fail with "Hardcover
  rejected the token"; create a new token and restart Fetchshelf.
- **Environment.** `FETCHSHELF_CONF_hardcover_token` and `FETCHSHELF_CONF_hardcover_token_file` set the two
  settings, and create the section if the file has none.
- **Beta.** Hardcover's API is still in beta, and Hardcover may change it or reset tokens without notice.

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
| `on_unlinked` | unset | What to do in qBittorrent to a torrent you **Unlink** from the library. The same table shape. |
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

[qbittorrent.on_unlinked]
category = "unlinked"
```

- **`path_mapping`** is needed when Fetchshelf and qBittorrent see the same folders under different
  paths, which is typical in Docker. The longest matching prefix wins, and it replaces just that prefix.
  Without a mapping, the paths qBittorrent reports are used as they are.
- **Categories** named in `on_cleaned`, `on_unlinked`, `[[tag]]` and `[[autograb]]` are created in qBittorrent
  automatically if they do not exist.
- Hardlinks only work when the download folder and the library folder are on the same filesystem and, in
  Docker, reach Fetchshelf through the same mount: two separate mounts count as different filesystems even
  on one disk. See [`method`](#library).

## The shared torrent filter

`[[autograb]]`, `[[snatchlist]]` and `[[tag]]` share one
set of optional filter fields. A torrent matches only if it satisfies **every** field that is set. A field
that is not set imposes no restriction (except `categories`, which always has defaults).

| Field | Description |
|---|---|
| `name` | A label for the block, shown in logs, events and the UI. Needed for `max_active_downloads`, `unsat_buffer` and `wedge_buffer` on an autograb. |
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

- Where possible the filter is translated into MAM search parameters so MAM narrows the results, and the
  **full filter is then applied again locally** to every result. The local check is what counts.
- Because `categories` defaults to "audio and ebook, nothing else", a block without `categories` never
  matches Musicology or Radio. To include them, set `categories = { musicology = true }` or the
  `radio` equivalent.
- Several `flags` may be set at once, so one block can both require and forbid flags.
- A `[[tag]]` rule can also be applied to torrents you already have, from the Config page. In that case
  fields that need live data (uploader, seeders, leechers, snatched counts) are looked up from MAM
  for each torrent.

## `[[autograb]]`

Repeatable. An autograb is a scheduled MAM search. Each run searches, filters the results, selects what
qualifies within your budget, and hands the selection to the downloader.

Takes all [shared filter](#the-shared-torrent-filter) fields, plus:

| Setting | Default | Description |
|---|---|---|
| `type` | (required) | What to search. See below. |
| `cost` | `"free"` | How to pay for matching torrents. See [cost](#cost). |
| `query` | unset | Free-text search. Supports MAM's search syntax. |
| `search_in` | `[]` | Which fields the query matches against: `author`, `description`, `filenames`, `file_types`, `narrator`, `series`, `tags`, `title`. Empty means MAM's default fields. |
| `sort_by` | newest first | `newest_first` (same as unset), `oldest_first`, `random`, `low_seeders` or `low_snatches`. |
| `max_pages` | 50 for `bookmarks`, `freeleech`, `mine`; 1 otherwise | Number of result pages to read per run. A page is 100 results (fixed). Paging stops early when a page comes back short. |
| `search_interval` | global `search_interval` | Minutes between runs. `0` means UI-triggered only. |
| `unsat_buffer` | global | Overrides the global buffer for this block. Requires `name`. |
| `wedge_buffer` | global | Overrides the global buffer for this block. Requires `name`. |
| `max_active_downloads` | unset | Cap on torrents selected by this block that have not finished downloading. Requires `name`. |
| `dry_run` | `false` | Search and run every selection check, but select, write and download nothing. What each torrent would get is logged. |
| `mark_removed` | `false` | For torrents Fetchshelf already stores: if the results show a gap where a stored torrent should be, ask MAM directly, and mark the torrent as removed from MAM if it is gone. It is accepted and validated, but nothing acts on it yet. |
| `category` | unset | qBittorrent category forced on everything this block downloads. Overrides `[[tag]]` rules. |

### `type`

| Value | Searches |
|---|---|
| `"bookmarks"` | Your MAM bookmarks. |
| `"freeleech"` | The global freeleech list. |
| `"new"` (alias `"torrents"`) | **All torrents** on MAM. The name refers to the default newest-first ordering, not to a separate set of torrents. |
| `"mine"` | Your own uploads. |
| `{ uploader = 12345 }` | One uploader's torrents, by MAM user id. |

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
- **Several blocks.** Blocks run on their own timers, and only one MAM-talking job runs at a time.
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

`cost` appears on `[[autograb]]`, `[[snatchlist]]` and the `grab` blocks of `[[hardcover_list]]`.

| Value | Meaning |
|---|---|
| `free` (default) | Only torrents that are free: VIP, personal freeleech or global freeleech. Others are not selected. |
| `wedge` | Use a freeleech wedge on torrents that are not already free. While wedges are at or below `wedge_buffer`, the torrent stays queued on the **Selected** page. |
| `try_wedge` | Use a wedge if one is available above `wedge_buffer`, otherwise download without one and pay with ratio. |
| `ratio` (alias `all`) | Pay with ratio. Free torrents are still free. |
| `metadata_only` | Record the torrent's metadata for books you already have, without downloading. |
| `metadata_only_add` | Like `metadata_only`, but also creates a metadata-only entry if the torrent is not in the library yet. Duplicate checks then treat that book as already in the library, so only a better format of it is downloaded. |

- A torrent that is VIP, personal freeleech or globally freeleech is **always** treated as free, whatever
  `cost` says, so wedges are never wasted.
- For `wedge` and `try_wedge` the wedge is applied at download time, not when the torrent is selected.
  `try_wedge` also skips the wedge when the torrent is already free.
- `metadata_only` and `metadata_only_add` never download anything, so ratio and unsat budgets do not apply
  to them.

## `[[snatchlist]]`

Repeatable. Reads one of your MAM snatch lists and refreshes the metadata of the torrents Fetchshelf already
stores. With `cost = "metadata_only_add"` it also records the others as metadata-only entries, so that
torrents you already have count as owned when Fetchshelf checks for duplicates. Nothing is downloaded.

Takes the [shared filter](#the-shared-torrent-filter), except that **`languages`, `uploaded_after` and
`uploaded_before` are not allowed** and cause an error.

| Setting | Default | Description |
|---|---|---|
| `type` | `unsat` | Which snatch list: `unsat`, `inact_unsat`, `seed_unsat`, `seed_sat`, `inact_sat` or `uploads_active`. |
| `cost` | `metadata_only` | `metadata_only` only refreshes torrents Fetchshelf already stores; `metadata_only_add` also records the others as metadata-only entries (see [cost](#cost)). No other cost is allowed. |
| `max_pages` | 50 | Pages of the snatch list to read. MAM decides how many rows a page holds; it has been seen sending 1000. Reading stops early when a page brings nothing new. |
| `search_interval` | global `search_interval` | Minutes between runs. |
| `dry_run` | `false` | Read the snatch list and work out every change, but record nothing. What would be recorded is logged. |

- **Stored torrents** are refreshed from the snatch list itself, with no further request to MAM. The list
  does not carry the language, media type, file count, upload date, description or ISBN, so those keep
  their stored values. Metadata you edited by hand is kept; only its VIP status follows MAM.
- **New torrents** (with `metadata_only_add`) are looked up on MAM one at a time, because the list does not
  say which language they are in. The first run on a long list therefore takes a while; later runs only
  look up what is new.

## `[[hardcover_list]]`

Repeatable. Follows the books with one reading status on [Hardcover](https://hardcover.app), such as *Want to
Read*, or the books on one of your Hardcover lists, and searches MAM for each of them. Needs
[`[hardcover]`](#hardcover).

| Setting | Default | Description |
|---|---|---|
| `name` | unset | Shown in logs, on the Jobs page and on the **Lists** page. Unnamed entries are called `hardcover_list[0]`, `hardcover_list[1]`, ... on the Jobs page. |
| `status` | — | A reading status: `want_to_read`, `currently_reading`, `read`, `paused` or `did_not_finish`. Set either `status` or `list`. |
| `list` | — | The slug of one of your Hardcover lists: the last part of its address, such as `books-to-fetch` in `hardcover.app/@you/lists/books-to-fetch`. |
| `prefer_format` | unset | `audio` or `ebook`. When both an audiobook and an ebook of a book are found, only this format is downloaded. Unset means both. |
| `search_interval` | global `search_interval` | Minutes between runs. |
| `dry_run` | `false` | Read the list and search MAM, but record and select nothing. What would be selected is logged. |
| `grab` | (at least one) | Repeatable `[[hardcover_list.grab]]` blocks, tried in order: a [`cost`](#cost) plus the [shared filter](#the-shared-torrent-filter). The first block that finds a format decides what that format costs. `metadata_only` costs are not allowed. |

```toml
[[hardcover_list]]
name = "Want to Read"
status = "want_to_read"
prefer_format = "audio"

[[hardcover_list.grab]]            # free copies of either format first
cost = "free"
languages = ["English"]

[[hardcover_list.grab]]            # then audiobooks worth a wedge
cost = "wedge"
languages = ["English"]
categories = { ebook = false }
```

What each run does:

1. **Reads the list** from Hardcover: title, authors (not narrators or illustrators), series and cover of each
   book. A book that has left the list is no longer searched for; one that comes back is searched again.
2. **Checks the library.** A book Fetchshelf already has, as an audiobook or an ebook, counts as found in that
   format, including books recorded by a snatch list.
3. **Searches MAM** by title and author for each format still missing, with each `grab` block in turn. The
   closest result must match the title and an author well enough; it then goes through the same selection
   rules as an autograb's: duplicates, preferred file types, `cost`, the unsatisfied-snatch budget and the
   [buffers](#top-level-settings).
4. **Waits a day** before searching again for a book that is still missing a format, so a long list doesn't
   mean a MAM search for every book on every run. New books are searched for on the next run.

- A format the grab blocks never look for (for example `categories = { ebook = false }` in every block) is
  not searched for at all.
- The **Lists** page shows every book and what was found for each format, and lets you mark a book done so
  it is no longer searched for.
- Selected torrents go to the **Selected** page and are downloaded by the Download job, as autograbs' are.

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
| `method` | global `link_method` | How files are placed. See below. |
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
| `hardlink_or_copy` (default) | Hard link, falling back to a copy. |
| `hardlink_or_symlink` | Hard link, falling back to a symbolic link. |
| `copy` | Copy the files. |
| `symlink` | Symbolic link. |
| `no_link` | Record metadata only; no files are placed. |

- From each torrent, only the files of its best audio format and its best ebook format are linked. The
  audio side uses `music_types` for Musicology, `radio_types` for Radio and `audio_types` for everything
  else; the ebook side uses `ebook_types`. A library's own `audio_types` replaces the audio-side list for
  every media type, Musicology and Radio included, and its own `ebook_types` replaces the ebook-side list.
- Files in subfolders named like `CD 1`, `Disc 2` or `Disk 3` go to a `Disc N` folder. Other folder
  structure is flattened.
- A `metadata.json` is written next to the files, for every media type. It is plain JSON in the format
  Audiobookshelf reads; see [`metadata.json`](user-guide.md#metadatajson) in the user guide for its
  fields.
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
it as a mismatch (*No library*, *Different library* or *Different folder*). **Refresh and relink** on the
torrent's page, or **Relink selected** on the Torrents page, links the book again from its download into
the library and folder it now belongs in. Changing `exclude_narrator_in_library_dir` alone does not count
as a mismatch.

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
url = "http://192.168.1.10:13378"
token = "abs-api-token"

[hardcover]
token = "hardcover-api-token"

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

# Search MAM for the books marked Want to Read on Hardcover.
[[hardcover_list]]
name = "Want to Read"
status = "want_to_read"
prefer_format = "audio"

[[hardcover_list.grab]]
cost = "free"
languages = ["English"]
```
