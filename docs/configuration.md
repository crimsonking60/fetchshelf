# Configuration reference

Fetchshelf is configured with a single TOML file, `config.toml`. This page lists every setting, what it
does and how the settings relate to each other. For working configs to start from, see
[`docs/config-examples/`](config-examples/README.md): five numbered examples, from a bare-bones `1-minimal.toml`
to `5-everything.toml`, which uses every option.

<!-- github-only:start -->
> [!NOTE]
> Development happens on `main`, so this page may describe changes that are not released yet. For the version you
> run, use the documentation linked under [Latest release](../README.md#latest-release) in the README.
<!-- github-only:end -->

## Contents

- [How the file is loaded](#how-the-file-is-loaded)
- [How the pieces fit together](#how-the-pieces-fit-together)
- [Top-level settings](#top-level-settings)
- [`[search]`](#search)
- [`[audiobookshelf]`](#audiobookshelf)
- [`[hardcover]`](#hardcover)
- [`[opds]`](#opds)
- [`[[qbittorrent]]`](#qbittorrent)
- [The shared torrent filter](#the-shared-torrent-filter)
- [`[[autograb]]`](#autograb)
- [Cost](#cost)
- [`[[hardcover_list]]`](#hardcover_list)
- [`[[file_list]]`](#file_list)
- [`[[bookmarks_list]]`](#bookmarks_list)
- [`[follow]`](#follow)
- [`[upgrade]`](#upgrade)
- [`[bonus]`](#bonus)
- [`[seeding]`](#seeding)
- [`[backup]`](#backup)
- [`[[tag]]`](#tag)
- [`[[library]]`](#library)
- [`[[notify]]`](#notify)
- [Reference data](#reference-data)
- [Complete example](#complete-example)

## How the file is loaded

- **Location.** The path comes from the `FETCHSHELF_CONFIG_FILE` environment variable. If it is not set,
  Fetchshelf uses a platform-appropriate config path, falling back to `config.toml` in the working
  directory. The file must have a `.toml` extension. The database is set up separately; see
  [Database setup](database-setup.md).
- **Missing file.** If the file does not exist, Fetchshelf creates one containing only an empty `mam_id`
  so the web UI still starts.
- **No `mam_id`.** With an empty `mam_id`, and no `mam_id_file`, the web UI runs but no background job runs.
- **Unknown keys are errors.** A misspelled or unsupported key stops the file from loading, and the error
  names the problem. This applies to every section.
- **Validation.** Required keys, mutually exclusive keys, costs, autograb `type`s, `sort_by` values,
  `search_in` fields, the top-level `languages`, block and library names, and every number are checked at startup. Numbers must be in
  the range the tables below give: for example `min_ratio` above 0, intervals from 0 to 43200 minutes, buffers
  0 or more, and a `min_` bound no larger than its `max_`. Values from environment overrides are checked the
  same way, and one that can't be read at all, such as `FETCHSHELF_CONF_search_interval=ten`, is named in the
  error. A config that fails validation is rejected as a whole, and Fetchshelf doesn't start; the error names
  the setting and, inside a section, which one (`the [[autograb]] 'Bookmarks'`, or `the 2nd [[autograb]]
  section` for one without a `name`).
- **Filter values are checked at startup too.** The names and values in every
  [shared torrent filter](#the-shared-torrent-filter) (media types, categories, languages, flags, sizes and
  dates) are checked for every `[[autograb]]`, `[[hardcover_list.grab]]`, `[[file_list.grab]]` and `[[bookmarks_list.grab]]` block and `[[tag]]`
  rule, including the top-level `languages` each block inherits. So is `[search] wedge_over`.
- **Checking a file before restarting.** See [Checking a configuration](#checking-a-configuration).
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

### Checking a configuration

`--check-config` loads the configuration the way startup does, environment overrides included, and either
prints what it understood or the reason Fetchshelf would not start. It contacts no service, leaves the database
alone, and is safe to run while Fetchshelf is running. To check an edited `config.toml` before restarting:

```sh
docker exec fetchshelf dotnet /app/Fetchshelf.Web.dll --check-config
```

To check a configuration against a new image before switching to it:

```sh
docker run --rm --entrypoint dotnet -v /path/to/config:/config:ro ghcr.io/crimsonking60/fetchshelf:<version> /app/Fetchshelf.Web.dll --check-config
```

This skips the image's start script, which would otherwise change the owner of the files in `/config`, and
mounts the folder read-only. Pass the same `FETCHSHELF_CONF_...` variables (`-e` or `--env-file`) if the
running container uses any, and mount whatever `mam_id_file` points to if it is outside `/config`.

The summary lists the libraries and every job under the name and schedule the **Jobs** page shows, and for each
[`[[file_list]]`](#file_list) whether its file is there and how many books it holds. Secrets appear only as set or
not set. The exit code is `0` when the configuration is good and `1` when it isn't, or when
there is no configuration file (which `--check-config` doesn't create).

## How the pieces fit together

Fetchshelf has two halves that can be used independently.

**Auto-downloader** — finds torrents on MAM and adds them to qBittorrent:

```
[[autograb]]        search MAM on a schedule        ───▶ selection ─▶ download ─▶ qBittorrent
                                                            ▲
[[hardcover_list]]  books you want, from Hardcover ─────────┤
                                                            │
[follow]            followed series and authors ────────────┘
```

- Every source decides **which** torrents qualify using the [shared torrent filter](#the-shared-torrent-filter)
  and **how** to pay for them using [`cost`](#cost).
- `ebook_types` / `audio_types` / ... decide which torrents have a usable file format at all, and which
  of two copies of the same book is better.
- `[[tag]]` rules (or an autograb's or Hardcover list's own `category`) decide which qBittorrent category
  and tags a new download gets. In the same way, `client` and `save_path` decide which `[[qbittorrent]]` gets it
  and which folder it is saved in; without them it goes to the first `[[qbittorrent]]`, into the folder its
  category or qBittorrent's default gives.
- `min_ratio`, `unsat_buffer` and `wedge_buffer` cap how much the downloader may spend. A block's own
  `unsat_buffer` / `wedge_buffer` (on a named `[[autograb]]`, a `[[hardcover_list]]`, `[follow]` or `[upgrade]`)
  override the global ones. The downloader reads them when it starts each
  torrent, so a changed value also applies to torrents already waiting on the **Selected** page.

**Library organizer** — turns finished downloads into a tidy folder tree:

```
qBittorrent (finished torrent) ─▶ [[library]] match ─▶ Author/Series/Title folder + metadata.json
          │                         by category or            ▲
          │                         download_dir              │ path_mapping translates the
          └─ category/tags set by [[tag]] rules ─────────────┘ client's paths to Fetchshelf's
```

- A `[[library]]` picks up torrents by their **qBittorrent category** (which a `[[tag]]` rule, or an
  autograb's or Hardcover list's `category`, sets) or by their **save path** (`download_dir`).
- `[[qbittorrent]]` `path_mapping` tells Fetchshelf where the client's files are from its own point of
  view, which matters in Docker.
- Every library, whatever its media types, gets the same folder tree and a `metadata.json` next to each
  book's files, readable by any app or script. A library can also get a `metadata.opf`, Calibre's format.
- `[audiobookshelf]` is optional; without it Audiobookshelf can still pick the books up from the
  metadata files.
- When a better format of a book is linked, the old copy is retired, and `on_cleaned` on the
  `[[qbittorrent]]` entry says what to do to it in the client.

With two qBittorrents, such as one for audiobooks on a large disk and one for ebooks, each gets a `name`, and the
blocks (or `[[tag]]` rules) say which one a download goes to. A `save_path` puts the downloads in a folder of their
own without a category path set up in qBittorrent; a library then accepts them by that folder (`download_dir`,
after `path_mapping`) or by their category:

```toml
[[qbittorrent]]
name = "audio"
url = "http://qbittorrent-audio:8080"
username = "admin"
password = "replace-with-your-qbittorrent-admin-password"

[[qbittorrent]]
name = "ebooks"
url = "http://qbittorrent-ebooks:8080"
username = "admin"
password = "replace-with-your-qbittorrent-admin-password"

[[tag]]                                     # audiobooks to the large disk
categories = { audio = true, ebook = false }
client = "audio"
save_path = "/big-disk/audiobooks"

[[tag]]
categories = { audio = false, ebook = true }
client = "ebooks"
category = "ebooks"

[[library]]
name = "Audiobooks"
download_dir = "/big-disk/audiobooks"       # as Fetchshelf sees it, after path_mapping
library_dir = "/library/audiobooks"

[[library]]
name = "Ebooks"
category = "ebooks"
library_dir = "/library/ebooks"
```

## Top-level settings

These go at the top of the file, before any section.

| Setting | Default | Description |
|---|---|---|
| `mam_id` | (required, or `mam_id_file`) | MAM session id: the cookie value created under your MAM security preferences. Empty means the UI runs but no jobs do. Treat it as a secret. |
| `mam_id_file` | unset | Full path of a file that holds the MAM session id, instead of writing it in `mam_id`. See [below](#reading-mam_id-from-a-file). |
| `web_host` | `"0.0.0.0"` | Address the web UI listens on. |
| `web_port` | `3157` | Port the web UI listens on, 1 to 65535. |
| `web_url` | unset | The address Fetchshelf's web UI answers at, such as `"https://fetchshelf.example.net"`: the address [`[[notify]]`](#notify) messages link to. Without it, messages carry no links. |
| `allowed_hosts` | `[]` | The host names Fetchshelf answers to, without a scheme, port or path, such as `["fetchshelf.lan", "books.example.org"]`. `*.example.org` allows its subdomains. Empty answers to any name. When set, `localhost`, `127.0.0.1`, `[::1]` and the address the container's health check uses are allowed too, and a request for any other name gets HTTP 400. See [Security and privacy](user-guide.md#security-and-privacy). |
| `min_ratio` | `2` | Fetchshelf never starts a download that would take your account below this ratio. Must be above 0. |
| `unsat_buffer` | `10` | Number of unsatisfied-snatch slots always left free. Can be overridden per block: a named `[[autograb]]`, a `[[hardcover_list]]`, `[follow]` or `[upgrade]`. |
| `wedge_buffer` | `0` | Fetchshelf never uses a freeleech wedge that would leave fewer than this many, including for **Wedge download** on the Search page. Can be overridden per block, as `unsat_buffer`. |
| `add_torrents_stopped` | `false` | Add torrents to qBittorrent in the stopped state. |
| `download_after_selection` | `true` | Start the Download job as soon as an autograb, a Hardcover list or the Search page selects a torrent. With `false`, selections wait on the **Selected** page for the next scheduled Download run, every `search_interval` minutes, which leaves time to look them over first. |
| `exclude_narrator_in_library_dir` | `false` | Leave the `{Narrator}` part out of library folder names. See [folder naming](#folder-naming). |
| `database_wal` | `false` | Use SQLite's write-ahead log, so pages stay responsive while a job writes. Only for a database on a local disk: on a network share it corrupts the database. Read at startup; turning it off again switches the database back. See [Database setup](database-setup.md). |
| `link_method` | `"hardlink_or_copy"` | How files are placed in every `[[library]]` that does not set its own `method`. Same values as [`method`](#library). |
| `metadata_json` | `true` | Whether every `[[library]]` that does not set its own `metadata_json` writes a `metadata.json`. See [`[[library]]`](#library). |
| `metadata_opf` | `false` | Whether every `[[library]]` that does not set its own `metadata_opf` writes a `metadata.opf`. See [`[[library]]`](#library). |
| `metrics` | `false` | Serve Prometheus metrics at `/metrics`. See [Prometheus metrics](#prometheus-metrics). |
| `covers_from_mam` | `0` | How many covers each Link run fetches from MAM for book folders that have no image, `0` to `100`. They are written as `cover.jpg` (or `cover.png`, `cover.webp`); books already in the library get theirs over the following runs. `0` fetches none. Series libraries are left out. The covers come from MAM's image server, which isn't among the parts of MAM its API documentation authorizes automating. See [Covers](user-guide.md#covers). |
| `search_interval` | `30` | Minutes between runs of each autograb block, and of the Download job. Overridable per block. `0` means the jobs only run from the **Jobs** page; at most 43200 (30 days). After a restart, a job's first run follows its last run, or comes within a few minutes of start for a job that is due. |
| `link_interval` | `10` | Minutes between scans for finished torrents to link into libraries. `0` to 43200, and the first run after a restart, as for `search_interval`. |
| `ignore_torrents` | `[]` | MAM torrent ids that must never be selected, as strings of digits, such as `["123456"]`. |
| `extra_categories` | `[]` | qBittorrent categories the web UI offers alongside the ones your `[[library]]` blocks use, such as `["seed-only"]`, for torrents that belong in no library. They appear, marked *no library*, in **Set category** and **Set category and tags** on the **Torrents** pages and in **Download into** on the Search page. A torrent in one is never linked, unless a library picks it up by its save path. |
| `languages` | `[]` | The [`languages`](#the-shared-torrent-filter) of every `[[autograb]]`, `[[hardcover_list.grab]]` and `[[follow.grab]]` block that doesn't set its own, and the language the Search page starts with. Empty means any language. **`[[tag]]` rules don't use it.** Same names as in the filter; see [languages](#languages). |
| `audio_types` | `["m4b", "m4a", "mp4", "mp3", "ogg"]` | File extensions wanted for Audiobooks and Periodical Audiobooks, best first. |
| `ebook_types` | `["epub", "cbz", "pdf", "mobi", "azw3", "azw", "cbr"]` | Same for Ebooks, Manga, Comic Books and Periodical Ebooks. |
| `music_types` | `["flac", "mp3", "pdf"]` | Same for Musicology. |
| `radio_types` | `["mp3"]` | Same for Radio. |
| `page_explanations` | `true` | Show a short explanation under each page title in the web UI. |
| `job_explanations` | `true` | Show a short description of each job on the Jobs page. |
| `dry_job_buttons` | `true` | Show **Dry run** next to **Run now** on the Jobs page for autograbs, Hardcover lists, Follow and Upgrade. `false` hides the buttons; `dry_run` in a block still works. |

Notes:

- **Format lists do three jobs.** A torrent is only downloaded if it contains at least one listed
  extension, only its best listed formats are linked into a library (see [`[[library]]`](#library)), and
  the list order ranks competing copies of the same book: the copy whose first listed extension comes
  earliest wins, and between two linked copies that rank the same, the larger one is kept. With
  `ebook_types = ["epub", "pdf"]`, an epub replaces a pdf of the same book. A `[[library]]` can override
  `audio_types` and `ebook_types` for linking.
- **Intervals** are per job: each `[[autograb]]` block runs on its own timer. A job
  with interval `0` is still listed on the Jobs page and can be started with **Run now**.
- **Buffers** are subtracted from your limits before anything is downloaded. If your unsat limit is 200,
  you have 150 unsatisfied snatches and `unsat_buffer` is 10, a run may add at most 40 torrents.
- **Ratio budget.** Free torrents cost no ratio. Others may only be downloaded while the remaining
  budget, `uploaded / min_ratio − downloaded − bytes already downloading`, covers their size. That is the
  most that can be downloaded while the ratio stays at `min_ratio`. The Download job logs the budget at the
  start of every run.

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

### Prometheus metrics

With `metrics = true`, `/metrics` answers in Prometheus's text format. Like the pages it has no login, and
`allowed_hosts` applies to it. A scrape reads only what Fetchshelf has stored or last read: it never calls MAM or
qBittorrent. A value not known yet, such as the account's numbers before the first read since Fetchshelf started, is
left out rather than reported as 0.

| Metric | Type | What it is |
|---|---|---|
| `fetchshelf_mam_ratio`, `fetchshelf_mam_uploaded_bytes`, `fetchshelf_mam_downloaded_bytes` | gauge | The account's ratio, upload and download, as last read from MAM. |
| `fetchshelf_mam_wedges`, `fetchshelf_mam_bonus_points` | gauge | Its freeleech wedges and bonus points. |
| `fetchshelf_mam_unsatisfied`, `fetchshelf_mam_unsatisfied_limit` | gauge | Its unsatisfied snatches, and how many it may have. |
| `fetchshelf_ratio_budget_bytes` | gauge | What may still be downloaded on ratio without going below `min_ratio`, as the **Account** page works it out. |
| `fetchshelf_mam_paused` | gauge | 1 while calls to MAM are paused after MAM refused one, else 0. |
| `fetchshelf_selected_waiting` | gauge | Selections waiting to be downloaded. |
| `fetchshelf_torrents{media_type, library}` | gauge | Torrents in the libraries. |
| `fetchshelf_grabbed_total{cost}` | counter | Torrents grabbed, by cost: `freeleech`, `vip`, `personal_freeleech`, `wedge` or `ratio`. |
| `fetchshelf_linked_total` | counter | Torrents linked into a library. |
| `fetchshelf_job_last_run_timestamp_seconds{job, result}` | gauge | When each job's last run ended (or started, while it runs), and how it ended. |
| `fetchshelf_job_failing{job}` | gauge | 1 while a job's runs keep failing, as the **Jobs** page marks it, else 0. |

A Prometheus scrape job:

```yaml
scrape_configs:
  - job_name: fetchshelf
    scrape_interval: 1m
    static_configs:
      - targets: ["fetchshelf:3157"]
```

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
| `interval` | `10` | Minutes between runs of the **Audiobookshelf** job, which matches linked books to Audiobookshelf items. `0` to 43200, as for `search_interval`. |
| `path_mapping` | `{}` | Table mapping a path prefix as Audiobookshelf sees it to the same location as Fetchshelf sees it, in the same order as qBittorrent's [`path_mapping`](#qbittorrent). |

```toml
[audiobookshelf]
url = "http://192.168.1.10:13378"
token = "replace-with-your-audiobookshelf-api-token"

[audiobookshelf.path_mapping]
"/media" = "/mnt/Data/media/abs"   # Audiobookshelf's path = Fetchshelf's path
```

What it does:

- The **matcher** looks for linked torrents that have no Audiobookshelf id yet, finds the Audiobookshelf
  library whose folders contain the book's library path, and stores the matching item's id. Audiobookshelf
  must therefore see the library at the same path as Fetchshelf, or `path_mapping` must say where
  Audiobookshelf sees it. A mapping lets Audiobookshelf mount only the library, without the downloads
  Fetchshelf needs for hardlinking. Fetchshelf translates a book's path by the longest Fetchshelf path that
  contains it, replacing just that part.
- **Validation.** Two Audiobookshelf paths can't map to the same Fetchshelf path. In 2026.11.5 the table was
  read the other way round, Fetchshelf's path = Audiobookshelf's path; a table that looks written in that
  order (its Fetchshelf side has nothing to do with any `library_dir`, while its Audiobookshelf side does)
  stops Fetchshelf from starting, with a message saying to swap the two sides.
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

## `[opds]`

Optional. Serves your ebook libraries as an [OPDS](https://opds.io) catalog at `http://<fetchshelf>/opds`,
for e-readers and reading apps such as KOReader, Moon+ Reader or Thorium. Leave the section out to serve
nothing; an empty `[opds]` serves the ebooks in every library. Audiobooks are never served. See
[Reading on an e-reader](user-guide.md#reading-on-an-e-reader-opds) in the user guide.

| Setting | Default | Description |
|---|---|---|
| `libraries` | `[]` | The `name`s of the `[[library]]` entries whose ebooks to serve. Empty serves the ebooks in every library; a library with only audiobooks has nothing to serve and doesn't appear in the catalog. |
| `username` | unset | With `password`, the login readers must give. Unset serves the catalog without one. Can't contain `:`. |
| `password` | unset | The login's password. A secret: the Config page hides it. |
| `password_file` | unset | A file holding the password, read like [`mam_id_file`](#reading-mam_id-from-a-file): a full path, its trailing newline ignored. Setting both `password` and `password_file` is an error. |

```toml
[opds]
libraries = ["Ebooks", "Comics"]
```

- **What is served.** Every linked book whose MAM media type is Ebook, Manga, Comic Book or Periodical Ebook,
  in a served library, with the ebook format Fetchshelf linked for it. Audiobooks, including an audiobook's
  companion PDF, are not served.
- **Validation.** Every name in `libraries` must be the `name` of a `[[library]]`; an unknown name stops
  Fetchshelf from starting. Libraries without a `name` can only be served by leaving `libraries` empty.
- **Login.** With `username` and a password, every `/opds` address asks for them with HTTP Basic, which OPDS
  readers support; a wrong or missing login gets HTTP 401. Only the catalog asks: the rest of Fetchshelf stays
  without a login. Setting `username` without a password, or a password without `username`, stops Fetchshelf
  from starting. A wrong login is logged with the address it came from, never with what was sent. HTTP Basic sends
  the password readably, so outside your home network, reach the catalog over HTTPS through a reverse proxy.
- **Environment.** `FETCHSHELF_CONF_opds_libraries` (comma-separated) sets `libraries`, and
  `FETCHSHELF_CONF_opds_username`, `FETCHSHELF_CONF_opds_password` and `FETCHSHELF_CONF_opds_password_file` the
  login. Each creates the section if the file has none.

## `[[qbittorrent]]`

Repeatable. Each entry is one qBittorrent Web UI. New downloads go to the **first** entry, unless the block that
selected them, or a `[[tag]]` rule, names another with `client`. Linking runs against every entry, and before a
download is added every entry is checked for it.

| Setting | Default | Description |
|---|---|---|
| `name` | unset | What a block's or `[[tag]]` rule's `client` refers to this entry by. Needed only then. Must be unique among the entries (ignoring case) and must not start or end with a space. |
| `url` | (required) | Web UI URL, for example `http://localhost:8080`. |
| `username` | (required) | Web UI user. |
| `password` | (required) | Web UI password. Treat it as a secret. |
| `path_mapping` | `{}` | Table mapping a path prefix as qBittorrent sees it to the same location as Fetchshelf sees it. Prefixes match whole folder names (`/downloads` doesn't match `/downloads2`), the longest matching prefix wins, and a trailing slash on either side makes no difference. |
| `on_cleaned` | unset | What to do in qBittorrent to a torrent whose files were retired in favour of a better format. A table with an optional `category` and a `tags` list; at least one of the two must be set. |
| `on_unlinked` | unset | What to do in qBittorrent to a torrent you **Unlink** from the library. The same table shape. |
| `on_invalid_torrent` | unset | What to do in qBittorrent to a torrent the Link job finds on MAM but can't read, because MAM's details hold a value Fetchshelf doesn't know, such as a new media type. The same table shape. See below. |

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
- **`on_invalid_torrent`** applies when the Link job looks a torrent up on MAM and finds it, but its details
  hold a media type, category, language or other value Fetchshelf doesn't know. The torrent can't be linked,
  and the Link error says what was applied. A **category** that no `[[library]]` uses is the useful choice:
  the torrent then no longer matches a library, so it isn't looked up again on every run. With only tags it
  is looked up, and fails, on every run as before. A torrent MAM doesn't have at all is not covered; it is
  usually from another tracker.
- **Categories** named in `on_cleaned`, `on_unlinked`, `on_invalid_torrent`, `[[tag]]`, `[[autograb]]` and
  `[[hardcover_list]]` are created in qBittorrent automatically if they do not exist.
- **A download queued for a `client` that is no longer configured** (the config changed after it was selected) goes
  to the first entry, with a warning in the log.
- Hardlinks only work when the download folder and the library folder are on the same filesystem and, in
  Docker, reach Fetchshelf through the same mount: two separate mounts count as different filesystems even
  on one disk. See [`method`](#library).

## The shared torrent filter

`[[autograb]]` and `[[tag]]` share one
set of optional filter fields. A torrent matches only if it satisfies **every** field that is set. A field
that is not set imposes no restriction (except `categories`, which always has defaults).

| Field | Description |
|---|---|
| `name` | A label for the block, shown in logs, events and the UI. Needed for `max_active_downloads`, `unsat_buffer` and `wedge_buffer` on an autograb. An `[[autograb]]` or `[[hardcover_list]]` runs as a job of this name, so the name must be unique among them (ignoring case), must not be `Link`, `Clean`, `Download`, `Audiobookshelf`, `Follow`, `Upgrade`, `Bonus`, `Backup`, `Seeding` or `Manual search`, must not look like `autograb[1]`, and must not start or end with a space. A block without a name is called `autograb[0]`, `hardcover_list[0]`, ... on the Jobs page. |
| `media_type` | List of media types. The torrent must be one of them. See [media types](#media-types). |
| `categories` | Per main type: `true` (all categories), `false` (none, so that type is excluded) or a list of category names. Keys: `audio`, `ebook`, `musicology`, `radio`. Defaults: `audio` and `ebook` are `true`, `musicology` and `radio` are `false`. See [categories](#categories). |
| `languages` | List of language names. The torrent's language must be in the list; a torrent whose language is unknown fails. An `[[autograb]]` or `[[hardcover_list.grab]]` block that leaves it out uses the top-level `languages`; `languages = []` means any language, whatever the top-level setting. **A `[[tag]]` rule never uses the top-level `languages`**: without its own, it matches every language. See [languages](#languages). |
| `flags` | Table of flag name to `true` (flag required) or `false` (flag forbidden). Flags you do not mention are ignored. See [flags](#flags). |
| `min_size`, `max_size` | [Sizes](#value-types-used-below), inclusive. |
| `exclude_uploader` | List of uploader names to reject. |
| `uploaded_after`, `uploaded_before` | [Dates](#value-types-used-below). Inclusive of the whole day. |
| `min_seeders`, `max_seeders` | Inclusive numeric bounds, 0 or more. |
| `min_leechers`, `max_leechers` | Inclusive numeric bounds, 0 or more. |
| `min_snatched`, `max_snatched` | Inclusive bounds on the number of times the torrent was completed. |
| `max_authors` | Maximum number of authors the torrent may list, inclusive, at least 1. Use it to skip anthologies. |

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
| `sort_by` | newest first | `newest_first` (same as unset), `oldest_first`, `random`, `low_seeders`, `low_snatches`, `high_seeders`, `high_snatches`, `largest_first` or `title`. Newest first is by upload date also with a `query`; for a `bookmarks` block it is MAM's own order, newest bookmark first. |
| `max_pages` | 50 for `bookmarks`, `freeleech`, `mine`; 1 otherwise | Number of result pages to read per run, at least 1. A page is 100 results (fixed). Paging stops early when a page comes back short. |
| `search_interval` | global `search_interval` | Minutes between runs, `0` to 43200. `0` means UI-triggered only. |
| `unsat_buffer` | global | Overrides the global buffer for this block. Requires `name`. |
| `wedge_buffer` | global | Overrides the global buffer for this block. Requires `name`. |
| `max_active_downloads` | unset | Cap on torrents selected by this block that are still waiting to be downloaded, at least 1. Requires `name`. |
| `dry_run` | `false` | Search and run every selection check, but select, write and download nothing. What each torrent would get is logged. It is listed under the run in the job's history too. **Dry run** on the Jobs page does one such run without this setting. |
| `mark_removed` | `false` | For torrents Fetchshelf already stores: if the results show a gap where a stored torrent should be, ask MAM directly, and mark the torrent as removed from MAM if it is gone. See [`mark_removed`](#mark_removed). Not for `type = "bookmarks"`. |
| `category` | unset | qBittorrent category forced on everything this block downloads. Overrides `[[tag]]` rules. |
| `client` | unset | The `name` of the [`[[qbittorrent]]`](#qbittorrent) that receives everything this block downloads. Unset: the first matching `[[tag]]` rule's, else the first `[[qbittorrent]]`. |
| `save_path` | unset | The folder qBittorrent saves everything this block downloads in, as qBittorrent sees it; an absolute path. Unset: the first matching `[[tag]]` rule's, else qBittorrent decides, from the category or its default. A library must accept the folder (`download_dir`) or the category for the download to be linked. |

### `type`

| Value | Searches |
|---|---|
| `"bookmarks"` | Your MAM bookmarks. A [`[[bookmarks_list]]`](#bookmarks_list) is the other way to download them. |
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

### `mark_removed`

MAM removes torrents now and then: replaced by a better upload, merged, or taken down. With
`mark_removed = true`, a block's run notices this for books Fetchshelf already stores:

- After reading its result pages, the run takes the lowest and highest MAM id among all results, before
  any filtering of Fetchshelf's own. A stored torrent whose MAM id lies in that range but which the results
  didn't include is a gap.
- Most gaps are not removals: MAM's search narrows by category, language and the query, so stored books that
  simply don't match the search fall inside the range of every page. Each gap is therefore looked up on MAM
  by its id, at most **20 per run**, and a torrent MAM still had is left alone for **7 days** before it is
  looked up again. Those looked up longest ago, or never, go first.
- A torrent MAM no longer has is marked as removed from MAM: it shows under *Problems* on the Torrents page,
  its page says so, and a *Removed from MAM* event is logged. Nothing is changed in qBittorrent or in the
  library: it keeps seeding and stays linked, and what to do with it is up to you.
- With `dry_run`, the lookups still happen, but nothing is marked; what would be is logged.

A `bookmarks` block can't use it: a bookmark list skips everything not bookmarked, so every stored torrent
in its range would look like a gap. Setting `mark_removed` on one stops startup with an error.

## Cost

`cost` appears on `[[autograb]]` and the `grab` blocks of `[[hardcover_list]]`, `[follow]` and `[upgrade]`. The
`grab` blocks take every value except the two `metadata_only` ones.

| Value | Meaning |
|---|---|
| `free` (default) | Only torrents that are free: VIP, personal freeleech or global freeleech. Others are not selected. |
| `wedge` | Use a freeleech wedge on torrents that are not already free. While wedges are at or below `wedge_buffer`, the torrent stays queued on the **Selected** page. It never downloads on ratio, so the ratio budget doesn't hold it back. |
| `try_wedge` | Use a wedge if one is available above `wedge_buffer`, otherwise download without one and pay with ratio. As it may pay with ratio, it waits while the ratio budget is too small for it. |
| `ratio` (alias `all`) | Pay with ratio. Free torrents are still free. |
| `metadata_only` | Record the torrent's metadata for books you already have, without downloading. |
| `metadata_only_add` | Like `metadata_only`, but also creates a metadata-only entry if the torrent is not in the library yet. Duplicate checks then treat that book as already in the library, so only a better format of it is downloaded. |

- A torrent that is VIP, personal freeleech or globally freeleech is **always** treated as free, whatever
  `cost` says, so wedges are never wasted.
- For `wedge` and `try_wedge` the wedge is applied at download time, not when the torrent is selected.
  `try_wedge` also skips the wedge when the torrent is already free. A torrent started with a wedge costs no
  ratio, so it isn't counted against the ratio budget while it downloads.
- `metadata_only` and `metadata_only_add` never download anything, so ratio and unsat budgets do not apply
  to them.

## `[[snatchlist]]` (removed)

`[[snatchlist]]` read MAM's snatch lists to refresh stored torrents and record the ones you own. It has been
removed, to conform to MAM's rules for automation: the snatch lists aren't among the parts of MAM its API
documentation authorizes automating. A config that still has a `[[snatchlist]]` section stops startup with a
message saying so; remove the sections. The torrents they recorded are kept. To record your own uploads as owned,
an `[[autograb]]` with `type = "mine"` and `cost = "metadata_only_add"` does it through the search.

## `[[hardcover_list]]`

Repeatable. Follows the books with one reading status on [Hardcover](https://hardcover.app), such as *Want to
Read*, or the books on one of your Hardcover lists, and searches MAM for each of them. Needs
[`[hardcover]`](#hardcover).

| Setting | Default | Description |
|---|---|---|
| `name` | unset | Shown in logs, on the Jobs page and on the **Lists** page. Unnamed entries are called `hardcover_list[0]`, `hardcover_list[1]`, ... on the Jobs page. The same rules as for an autograb's [`name`](#the-shared-torrent-filter) apply. |
| `status` | — | A reading status: `want_to_read`, `currently_reading`, `read`, `paused` or `did_not_finish`. Set either `status` or `list`. |
| `list` | — | The slug of one of your Hardcover lists: the last part of its address, such as `books-to-fetch` in `hardcover.app/@you/lists/books-to-fetch`. |
| `prefer_format` | unset | `audio` or `ebook`. When both an audiobook and an ebook of a book are found, only this format is downloaded. Unset means both. |
| `category` | unset | qBittorrent category forced on everything this list downloads. Overrides `[[tag]]` rules. Use it to send different lists to different libraries. |
| `client` | unset | The `name` of the [`[[qbittorrent]]`](#qbittorrent) that receives everything this list downloads. Unset: the first matching `[[tag]]` rule's, else the first `[[qbittorrent]]`. |
| `save_path` | unset | The folder qBittorrent saves everything this list downloads in, as qBittorrent sees it; an absolute path. Unset: the first matching `[[tag]]` rule's, else qBittorrent decides, from the category or its default. A library must accept the folder (`download_dir`) or the category for the download to be linked. |
| `search_interval` | global `search_interval` | Minutes between runs. |
| `unsat_buffer` | global | Overrides the global buffer for what this list selects. |
| `wedge_buffer` | global | Overrides the global buffer for what this list selects. |
| `max_active_downloads` | unset | Cap on torrents this list selected that are still waiting to be downloaded, at least 1. |
| `dry_run` | `false` | Read the list and search MAM, but record and select nothing. What would be selected is logged. It is listed under the run in the job's history too. **Dry run** on the Jobs page does one such run without this setting. |
| `grab` | (at least one) | Repeatable `[[hardcover_list.grab]]` blocks, tried in order: a [`cost`](#cost) plus the [shared filter](#the-shared-torrent-filter). Each format goes to the first block that finds a torrent its `cost` accepts: a `free` block only takes free torrents and leaves the rest to the next blocks. `metadata_only` costs are not allowed. |

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
   book, and the ISBNs and ASINs of its 20 most read editions and its default audiobook, ebook and print editions.
   A book that has left the list is no longer searched for; one that comes back is searched again.
2. **Checks the library.** A book Fetchshelf already has, as an audiobook or an ebook, counts as found in that
   format, including books recorded as metadata only.
3. **Searches MAM** by title and author for each format still missing, with each `grab` block in turn. A
   result whose ISBN or ASIN is one of the book's is the book, however its title is written, as for a translation
   or a subtitle; ISBN-10 and ISBN-13 count as the same. Otherwise the closest result must match the title and an
   author well enough. When the first 100 results hold nothing like the book, as for a title of very common words,
   the next 100 are read once. Either way, the block's `cost` must accept the result: a
   `free` block takes only a VIP, freeleech or personal-freeleech torrent and leaves any other to the next
   block. The torrent then goes through the same selection rules as an autograb's: duplicates, preferred file
   types, the unsatisfied-snatch budget and the [buffers](#top-level-settings). A book that only blocks with too
   low a cost found shows as *costs more than the list's grab blocks allow* on the **Lists** page.
4. **Waits a day** before searching again for a book that is still missing a format, so a long list doesn't
   mean a MAM search for every book on every run. Once MAM has had nothing for a book for a week, it waits a week
   instead: a book that matches but costs too much, or that was left out for the other format, counts as found.
   New books are searched for on the next run, and **Search again** on the **Lists** page has the next run search
   one book whenever it was last searched.
5. **Looks again for what went away.** A format whose selection was taken out of the **Selected** queue before it
   was downloaded (a stale link, MAM no longer having the torrent, or removed by hand) is searched for again, as is
   one whose copy is no longer in the library (its record removed). A format the selection's duplicate check marked
   as selected or in the library, because another copy of the book was, stays as it is.

- A format the grab blocks never look for (for example `categories = { ebook = false }` in every block) is
  not searched for at all.
- The **Lists** page shows every book and what was found for each format, and lets you mark a book done so
  it is no longer searched for.
- Selected torrents go to the **Selected** page and are downloaded by the Download job, as autograbs' are.

## `[[file_list]]`

Repeatable. Reads wanted books from a CSV or text file, such as a Goodreads or StoryGraph export or a list
typed by hand, and searches MAM for each of them like a [`[[hardcover_list]]`](#hardcover_list). Needs no
account or API.

| Setting | Default | Description |
|---|---|---|
| `name` | unset | Shown in logs, on the Jobs page and on the **Lists** page. Unnamed entries are called `file_list[0]`, `file_list[1]`, ... on the Jobs page. The same rules as for an autograb's [`name`](#the-shared-torrent-filter) apply. |
| `path` | — | The list file. A relative path is relative to the folder `config.toml` is in, so under Docker a file next to it in `/config` is `path = "wanted.csv"`. |
| `shelf` | unset | For an export that holds every shelf: keep only the rows on this shelf, compared ignoring case. Read from the `Exclusive Shelf` column of a Goodreads export, the `Read Status` column of a StoryGraph one, or a `Shelf` column. A file without such a column is read whole, with a warning in the log. |
| `prefer_format` | unset | As for [`[[hardcover_list]]`](#hardcover_list). |
| `category` | unset | As for [`[[hardcover_list]]`](#hardcover_list). |
| `client` | unset | As for [`[[hardcover_list]]`](#hardcover_list). |
| `save_path` | unset | As for [`[[hardcover_list]]`](#hardcover_list). |
| `search_interval` | global `search_interval` | Minutes between runs. |
| `unsat_buffer` | global | Overrides the global buffer for what this list selects. |
| `wedge_buffer` | global | Overrides the global buffer for what this list selects. |
| `max_active_downloads` | unset | Cap on torrents this list selected that are still waiting to be downloaded, at least 1. |
| `dry_run` | `false` | As for [`[[hardcover_list]]`](#hardcover_list). |
| `grab` | (at least one) | Repeatable `[[file_list.grab]]` blocks, as for [`[[hardcover_list]]`](#hardcover_list). |

```toml
[[file_list]]
name = "Goodreads to-read"
path = "goodreads_library_export.csv"
shelf = "to-read"
prefer_format = "audio"

[[file_list.grab]]
cost = "free"
languages = ["English"]
```

The file is read on every run, so changes to it are picked up without a restart. A file that isn't there
when Fetchshelf starts is no error, as it may be put there later; until it is, the list's job fails with
*List file not found*. `--check-config` says whether each list file is there and how many books it holds.

What the file may hold, told apart by its content:

- **A CSV file** whose first line names its columns, one of them `Title`, separated by commas or semicolons.
  Columns are found by name, ignoring case, and the others are ignored:
  - `Title` (required). A series written in the title as Goodreads writes it, `Dune (Dune, #1)`, is taken off
    the title.
  - `Author` plus `Additional Authors` (Goodreads), or `Authors` (StoryGraph, separated by commas). Several
    authors in one `Author` cell are separated by `;` or `&`.
  - `Series` and `Series Number` (or `Number`), when the file has them.
  - `ISBN`, `ISBN13` or `ISBN/UID`: a MAM upload carrying the book's ISBN is the book, as for a Hardcover list.
  - `Exclusive Shelf`, `Read Status` or `Shelf`: for `shelf`.
- **A text file** with one book per line: `Title by Author`, `Title - Author`, or just the title. Blank lines
  and lines starting with `#` are skipped. See
  [`wanted-books.txt`](config-examples/wanted-books.txt).

A book keeps its place on the **Lists** page however the file changes: it is known by its ISBN, else by its
title and first author, so the file can be exported again or reordered. A book in the file twice is one book. A
row that can't be read, with no title or a quote that is never closed, is left out: the run's result counts
such lines and the log names them. A book taken out of the file is no longer searched for.

Everything after reading the file is as for a [`[[hardcover_list]]`](#hardcover_list): the library is checked,
MAM is searched with each `grab` block in turn, and a book still missing a format is searched for again a day
later. The list is known by its `name`: renaming the file keeps the list's books and what was found for them,
while renaming the list starts a new one.

## `[[bookmarks_list]]`

At most one. Shows the torrents bookmarked on MAM on the **Lists** page, with what happened to each, and
downloads them through its `grab` blocks. A bookmark names a torrent rather than a book, so nothing is searched
for: each run reads the bookmarks, newest first, and puts each torrent through the `grab` blocks and the usual
selection rules.

| Setting | Default | Description |
|---|---|---|
| `name` | unset | Shown in logs, on the Jobs page and on the **Lists** page. Unnamed, the list and its job are called `bookmarks_list`. |
| `category` | unset | As for [`[[hardcover_list]]`](#hardcover_list). |
| `client` | unset | As for [`[[hardcover_list]]`](#hardcover_list). |
| `save_path` | unset | As for [`[[hardcover_list]]`](#hardcover_list). |
| `search_interval` | global `search_interval` | Minutes between runs. |
| `unsat_buffer` | global | Overrides the global buffer for what this list selects. |
| `wedge_buffer` | global | Overrides the global buffer for what this list selects. |
| `max_active_downloads` | unset | Cap on torrents this list selected that are still waiting to be downloaded, at least 1. |
| `dry_run` | `false` | As for [`[[hardcover_list]]`](#hardcover_list). |
| `grab` | (at least one) | Repeatable `[[bookmarks_list.grab]]` blocks, tried in order: a [`cost`](#cost) plus the [shared filter](#the-shared-torrent-filter). A bookmark goes to the first block whose filter takes it and whose `cost` accepts it. `metadata_only` costs are not allowed. |

`prefer_format` is refused: a bookmark is one torrent, so of one format already. An audiobook bookmark is
looked at as an audiobook only, an ebook bookmark as an ebook only; a bookmark that is neither is left out.

```toml
[[bookmarks_list]]
name = "MAM bookmarks"
search_interval = 60

[[bookmarks_list.grab]]            # free bookmarks first
cost = "free"

[[bookmarks_list.grab]]            # then small ones, paid with ratio
cost = "ratio"
max_size = "30 MiB"
```

What each run does:

1. **Reads the bookmarks** from MAM, through its search, 100 to a page. A torrent that is no longer bookmarked
   leaves the list; bookmarked again, it comes back.
2. **Checks the library**, as a Hardcover list does: a book Fetchshelf already has in the torrent's format counts
   as in the library.
3. **Tries the `grab` blocks** in order. The torrent goes to the first block whose filter takes it and whose
   `cost` accepts it, then through the same selection rules as an autograb's. A bookmark a filter takes but no
   cost accepts shows as *costs more than the list's grab blocks allow*; one no filter takes shows as *none of the
   list's grab blocks' filters takes it*.
4. **Looks at every bookmark not yet selected or in the library again on the next run.** That costs no extra
   search, so a bookmark that turns freeleech is taken on the next run.

A `[[bookmarks_list]]` and an `[[autograb]]` with `type = "bookmarks"` can't both be configured: both would
select the same torrents. Choose the list to see each bookmark's state on the **Lists** page, or the autograb for
what only it has: `sort_by`, `max_pages`, a `query`, and one job per cost level.

## `[follow]`

Optional. Lets series and authors be followed: the Follow job searches MAM for them and lists the books the
library is missing on the **Following** page, or, for those set to *Auto*, selects them for download. Which
series and authors are followed, and which of them are on *Auto*, is chosen in the web UI (the **Series** page,
a torrent's page, the Torrents page's author and series filters, the **Following** page), not here.
Without this section there is no Follow job and no follow buttons; the **Series** page still shows what is
missing.

| Setting | Default | Description |
|---|---|---|
| `search_interval` | global `search_interval` | Minutes between runs of the Follow job, `0` to 43200. `0` means it runs only from the Jobs page. |
| `searches_per_run` | `10` | Series and authors one run searches at most. Each search reads as many pages of results as it needs, 100 torrents to a page (see below). |
| `search_again_after` | `24` | Hours before the same series or author is searched again, `1` to `720`. |
| `prefer_format` | unset | `audio` or `ebook`. When a book is found in both formats, only this one is taken, unless only the other one has a copy a `grab` block's `cost` accepts. Unset means both. |
| `category` | unset | qBittorrent category forced on everything the Follow job downloads, and on downloads from the **Following** page. Overrides `[[tag]]` rules. |
| `client` | unset | The `name` of the [`[[qbittorrent]]`](#qbittorrent) that receives everything the Follow job downloads, and downloads from the **Following** page. Unset: the first matching `[[tag]]` rule's, else the first `[[qbittorrent]]`. |
| `save_path` | unset | The folder qBittorrent saves the Follow job's downloads in, and those from the **Following** page, as qBittorrent sees it; an absolute path. Unset: the first matching `[[tag]]` rule's, else qBittorrent decides, from the category or its default. A library must accept the folder (`download_dir`) or the category for the download to be linked. |
| `unsat_buffer` | global | Overrides the global buffer for what the Follow job and the **Following** page selects. |
| `wedge_buffer` | global | Overrides the global buffer for what the Follow job and the **Following** page selects. |
| `max_active_downloads` | unset | Cap on torrents of every followed series and author together that are still waiting to be downloaded, at least 1. |
| `dry_run` | `false` | Search MAM, but record and select nothing. What would be selected is logged. It is listed under the run in the job's history too. **Dry run** on the Jobs page does one such run without this setting. |
| `grab` | (at least one) | Repeatable `[[follow.grab]]` blocks, tried in order: a [`cost`](#cost) plus the [shared filter](#the-shared-torrent-filter), as in [`[[hardcover_list]]`](#hardcover_list). A book goes to the first block whose filter and `cost` accept it. `metadata_only` costs are not allowed. Blocks without `languages` get the top-level `languages`. |
| `audiobookshelf_series` | `"off"` | `"follow"` or `"auto"`: follow every series in Audiobookshelf, in that mode, so series from before Fetchshelf are completed too. Needs [`[audiobookshelf]`](#audiobookshelf). See [series from Audiobookshelf](#series-from-audiobookshelf). |
| `audiobookshelf_libraries` | `[]` | The Audiobookshelf libraries whose series are followed, by name, ignoring case. Empty means every book library. Needs `audiobookshelf_series`. |

```toml
[follow]
searches_per_run = 10
search_again_after = 24
prefer_format = "audio"
category = "audiobooks"

[[follow.grab]]
cost = "free"
categories = { ebook = false }
```

What each run does:

1. **Picks the follows that are due**: those not searched within `search_again_after` hours, the
   longest-waiting first, up to `searches_per_run`. Ignored series are never searched.
2. **Searches MAM** for each, in the series (or author) field, newest first, in the languages and main
   categories the `grab` blocks allow, reading the results 100 to a page. The first search of a series or
   author reads every page, however many there are, and so does one every 30 days after that. The searches in
   between stop at the page that reaches torrents uploaded before the previous search (less a day, for uploads
   MAM shows late), since that search saw the rest: a long-running author then costs one or two pages a day,
   not all of them. MAM matches words, so a result only counts when its series (or author) is the followed one,
   compared the way the library compares names.
3. **Leaves out what the library has**: the same torrent, the same book (as selection judges a duplicate), a
   series entry already owned in that format and language, and anything already on the **Selected** page.
4. **Decides each book, language and format on its own**, a series in entry order and an author's books newest first:
   the first `grab` block whose filter and `cost` accept a torrent takes the one in the best format. A series or author on *Auto* has
   it selected, through the usual rules and the unsatisfied-snatch budget; a followed one has it listed on the
   **Following** page. A match no block's `cost` accepts is listed as *not free*.

With the defaults, a run searches at most 10 series or authors, and each series or author on the **Following**
page is searched once a day. A search that fails is recorded on the **Errors** page and waits its turn like one that worked, so it
doesn't take a search from the others on every run. A run that reaches the unsatisfied-snatch budget stops selecting; the follow it was on is searched first
on the next run.

### Series from Audiobookshelf

With `audiobookshelf_series`, each run of the Follow job first reads the series of Audiobookshelf's book libraries
(podcast libraries are left out), before it picks the follows that are due:

- **Each series not followed in any mode is followed**: `"follow"` lists what is found on the **Following** page,
  `"auto"` selects it. The follow says *from Audiobookshelf* on the **Following** page. A series already followed,
  on *Auto* or ignored keeps its mode, and one that leaves Audiobookshelf stays followed: the follow is yours from
  then on, to change or stop like any other. A series you stop following isn't followed again from Audiobookshelf;
  follow it again from the **Series** page to take it back.
- **The books Audiobookshelf has count as owned**, in the format it has them (an audiobook, an ebook, or both; an
  item with neither counts as both), so the Follow job doesn't search for or download what is already there. The
  **Series** page lists Audiobookshelf's series too, with the entries it has. Books Fetchshelf linked itself, and
  matched to their Audiobookshelf item, are counted once.
- **A name with a number in it**, as Audiobookshelf sometimes keeps from a folder name (`Earthseed #1, 2`), is
  followed under the name before the ` #`.
- **The searches spread over many runs.** A library of a thousand series adds a thousand follows on the first
  run, but each run still searches only `searches_per_run` of them, the ones never searched first. Raise
  `searches_per_run` or lower `search_interval` to get through them sooner; each search is one or more MAM requests.
- If Audiobookshelf can't be reached, the run says so in the log and searches the follows it has.
- A name in `audiobookshelf_libraries` that is no book library in Audiobookshelf is logged with the names there are.
- A dry run reads Audiobookshelf and says how many series it would follow, but follows none.

```toml
[follow]
audiobookshelf_series = "follow"
audiobookshelf_libraries = ["Audiobooks"]

[[follow.grab]]
cost = "free"
```

Authors are not followed from Audiobookshelf: an author follow searches every upload by that author, which for a
whole library would be too many searches.

A book in two languages is two books: each language's edition is found, owned and decided on its own, and each `grab`
block's `languages` says which editions it takes. With these blocks, every English book of a followed series that the
library lacks in English is found, and every Hungarian one it lacks in Hungarian too, taken when it is free and listed as
*not free* otherwise:

```toml
languages = ["English"]

[[follow.grab]]          # Hungarian editions, when they are free
cost = "free"
languages = ["Hungarian"]

[[follow.grab]]          # English editions, paid with ratio (the block inherits the top-level languages)
cost = "ratio"
```

`prefer_format` keeps one format per language. Owning the English *Discworld 1-40* collection doesn't make the
Hungarian editions owned. The **Series** page still counts missing books in any language.

## `[upgrade]`

Optional. The Upgrade job searches MAM for better formats of books in the library: a book whose linked copy is in
a format that isn't the first in its format list (`audio_types`, `ebook_types`, `music_types`, `radio_types`, or
its library's own `audio_types`/`ebook_types`), such as an mp3 audiobook with `audio_types = ["m4b", "mp3"]`. A better copy goes through the usual selection
rules, and the Clean job retires the old one once the new one is linked. Without this section there is no
Upgrade job; better copies other searches happen to find are still taken.

| Setting | Default | Description |
|---|---|---|
| `search_interval` | global `search_interval` | Minutes between runs of the Upgrade job, `0` to 43200. `0` means it runs only from the Jobs page. |
| `searches_per_run` | `20` | MAM searches one run makes at most: one per book. |
| `search_again_after` | `30` | Days before the same book is searched again, `1` to `365`, whether a better copy was found or not. |
| `libraries` | `[]` | Only books in these [`[[library]]`](#library) names. Empty means every library. |
| `client` | unset | The `name` of the [`[[qbittorrent]]`](#qbittorrent) that receives the better copies. Unset: the first matching `[[tag]]` rule's, else the first `[[qbittorrent]]`. |
| `save_path` | unset | The folder qBittorrent saves the better copies in, as qBittorrent sees it; an absolute path. Unset: the first matching `[[tag]]` rule's, else qBittorrent decides, from the category or its default. A library must accept the folder (`download_dir`) or the category for the download to be linked. |
| `unsat_buffer` | global | Overrides the global buffer for what the Upgrade job selects. |
| `wedge_buffer` | global | Overrides the global buffer for what the Upgrade job selects. |
| `max_active_downloads` | unset | Cap on torrents the Upgrade job selected that are still waiting to be downloaded, at least 1. |
| `dry_run` | `false` | Search MAM, but select nothing. What would be selected is logged. It is listed under the run in the job's history too. **Dry run** on the Jobs page does one such run without this setting. |
| `grab` | one `free` block | Repeatable `[[upgrade.grab]]` blocks, tried in order: a [`cost`](#cost) plus the [shared filter](#the-shared-torrent-filter). Without any, one block with `cost = "free"` and no filter is used, so upgrades only take free copies. `metadata_only` costs are not allowed. The top-level `languages` doesn't apply: a book is searched in its own language. |

What each run does: it picks the books that could be better, never-searched first and then the longest ago,
searches MAM for each by its title and authors (as a Hardcover list searches) in its language and media type, and
keeps the results that are the same book (as for duplicates) in a better format. The best of them that a `grab`
block accepts is selected; when selection turns it down, the next is tried. A torrent already known to Fetchshelf,
queued, or in `ignore_torrents` is left out. It is selected with the old copy's qBittorrent category when that is a
library's. A run stops selecting at the unsatisfied-snatch budget.

```toml
[upgrade]
libraries = ["Audiobooks"]

[[upgrade.grab]]
cost = "free"

[[upgrade.grab]]            # pay a wedge for a better copy of an audiobook
cost = "wedge"
categories = { ebook = false }
```

## `[bonus]`

Optional. The Bonus job spends the account's bonus points: a wedge when one is due, then a VIP top-up, then upload
credit, as long as the points above `keep_points` pay for it. Points stop at MAM's cap of 99,999, and those earned
above it are lost, so spending them in time costs nothing. Upload credit raises the ratio, and with it the
[ratio budget](user-guide.md#protecting-your-account); wedges are what the `wedge` [costs](#cost) spend. Without this
section no points are ever spent.

> [!WARNING]
> The Bonus job spends points without asking. Set `dry_run = true` first and check the log, or the Account page's
> **Bonus points** section, to see what it would buy.

| Setting | Default | Description |
|---|---|---|
| `bonus_interval` | `120` | Minutes between runs of the Bonus job: `0` (only from the Jobs page) or `60` to 43200 (30 days). Set only here: the top-level `search_interval` is for jobs that search MAM and doesn't apply. |
| `keep_points` | `0` | Upload credit and VIP are only bought with points above this. A wedge may use them: `keep_points = 55000` keeps a wedge's worth of points, plus 5,000, from upload credit. |
| `upload_amounts` | `[100, 50, 20, 5, 1]` | How many GB of upload credit one purchase buys, tried largest first, each as often as the points above `keep_points` allow. Whole numbers from 1 to 100; MAM sells 1, 5, 20, 50 and 100. `[]` buys no upload credit. |
| `wedge_every` | `0` | Hours between wedge purchases, up to 8760. `0` buys no wedges. A wedge costs 50,000 points. |
| `vip` | `false` | Top VIP up to the most MAM allows, once a run, when the points above `keep_points` are at least 15,000. |
| `max_purchases_per_run` | `10` | Purchases one run makes at most, `1` to `100`. |
| `dry_run` | `false` | Decide what to buy and log it, but buy nothing. |

What each run does: it reads the account's points, then buys one thing at a time. A wedge comes first, when
`wedge_every` hours (less ten minutes) have passed since the last wedge bought with points. Upload credit costs 500
points per GB. Each purchase goes by the points MAM says are left after the last one, and purchases are at least
ten seconds apart, since MAM refuses purchases that come too fast; such a refusal ends the run's purchases until the
next run. Every purchase is on the **Events** page, and can be [notified](#notification-events) as `bonus_spent`.

```toml
[bonus]
keep_points = 55000          # keep a wedge's worth of points, plus 5,000
upload_amounts = [100, 50]
wedge_every = 24             # one wedge a day
```

## `[seeding]`

Optional. Runs the Seeding job, which estimates from qBittorrent's seeding time which of Fetchshelf's torrents still
need seeding before MAM counts them as satisfied. It reads qBittorrent only, never MAM: MAM's snatch lists, which say
it per torrent, aren't among the parts of MAM that may be automated. See
[Seeding torrents until MAM counts them as satisfied](user-guide.md#seeding-torrents-until-mam-counts-them-as-satisfied).

| Setting | Default | Description |
|---|---|---|
| `seed_hours` | `72` | Hours of seeding MAM requires before a snatch is satisfied, `1` to `8760`. Check MAM's rules. |
| `within_days` | `30` | Days after the snatch in which `seed_hours` must be reached, `1` to `365`. |
| `interval` | `60` | Minutes between runs of the **Seeding** job, `0` to 43200. `0`: only from the Jobs page. |
| `tag_unsatisfied` | unset | A qBittorrent tag put on Fetchshelf's torrents that aren't satisfied, and taken off once they are. One tag, without a comma. |
| `warn_before_removal` | `true` | Ask before **Unlink** and **Remove from qBittorrent with data** on a torrent that isn't satisfied, and hold the Clean job's `on_cleaned` back for it until it is. |

- **How it estimates.** A torrent is satisfied once qBittorrent's `seeding_time` reaches `seed_hours`, and stays
  so. Until then it is unsatisfied, seeding or not, while within `within_days` of when qBittorrent added it, and
  past the period after that. A torrent still downloading isn't estimated, nor one never seen in qBittorrent.
- **Only an estimate.** Snatches made outside Fetchshelf or in another client, and seeding time MAM counts
  differently, make it differ from MAM's counts, which the **Account** page shows next to it.
- **`on_cleaned` held back.** With `warn_before_removal`, a superseded copy that isn't satisfied is retired as usual,
  its files leaving the library, but `on_cleaned` waits; the Seeding job applies it once the copy is satisfied.

```toml
[seeding]
tag_unsatisfied = "unsatisfied"
```

## `[backup]`

Optional. Backs up the database while Fetchshelf runs, with SQLite's online backup, which takes a consistent copy
even while jobs write, with or without [`database_wal`](database-setup.md#database_wal). Also backs up before a new
version changes the database. Without this section Fetchshelf takes no backups. See
[Backups and moving to a new machine](user-guide.md#backups-and-moving-to-a-new-machine) for restoring one.

| Setting | Default | Description |
|---|---|---|
| `folder` | (required) | Where backups go. A relative path is relative to the database's folder, so `folder = "backups"` is `/database/backups` in Docker. It can't be the database's own folder. A folder on another disk is safer: a backup on the same disk is lost with it. |
| `interval` | `1440` | Minutes between runs of the **Backup** job, `0` to 43200 (30 days). `0`: backups only before upgrades and from the Jobs page. |
| `keep` | `7` | How many of the Backup job's backups are kept, `1` to `365`. After each one, the oldest beyond that are deleted. |
| `before_upgrade` | `true` | Back up at startup when the new version is about to change the database. These are kept apart from `keep`: the last 3. |

- **Names.** Each backup is one file, `data-<UTC date and time>-<version>-<reason>.db`. The version is the one to
  run on the backup: the version that last started on the database. `data-20261205-031500-2026.12.1-scheduled.db`
  is from the Backup job, and `data-20261210-031500-2026.12.1-before-2026.12.2.db` was taken before upgrading from
  2026.12.1 to 2026.12.2. A backup of a database no release after 2026.11.11 has started on has no version in
  its name, as in `data-20261111-031500-scheduled.db`. A backup holds everything and needs no `-wal` or `-shm` file
  next to it. Fetchshelf only ever deletes files named like this, so the folder can hold other files too.
- **Checked before it counts.** A backup is written under a temporary name, checked with SQLite's integrity check,
  and only then given its name. One that fails is deleted, and the Backup job's run fails, which
  [`job_failing`](#notification-events) reports like any other job.
- **Before an upgrade.** At startup, before the database is changed, Fetchshelf backs it up when a new version has
  changes to make to it. A new database, or one the version already knows, isn't backed up. If that backup fails,
  Fetchshelf doesn't start, and the log says why: upgrading without the promised copy would be worse. To start
  anyway, set `before_upgrade = false`.
- **Checked at startup.** A missing `folder`, the database's own folder, and `interval` or `keep` out of range stop
  Fetchshelf from starting, and [`--check-config`](#checking-a-configuration) reports them.
- **Not backed up:** `config.toml`, the libraries and the downloads.

```toml
[backup]
folder = "/backups/fetchshelf"   # another disk, mounted into the container
keep = 14
```

## `[[tag]]`

Repeatable. A tag rule is the [shared filter](#the-shared-torrent-filter) plus what to do in qBittorrent
with matching downloads.

| Setting | Default | Description |
|---|---|---|
| `category` | unset | qBittorrent category to assign. |
| `tags` | `[]` | qBittorrent tags to add. |
| `client` | unset | The `name` of the [`[[qbittorrent]]`](#qbittorrent) to add the download to. |
| `save_path` | unset | The folder qBittorrent saves the download in, as qBittorrent sees it; an absolute path. |

For each new download:

- The **category** comes from the **first** matching rule that sets one.
- The **tags** are the **union** of the tags from **all** matching rules.
- An autograb's or Hardcover list's own `category` overrides the rules' category.
- **`client`** and **`save_path`** work as the category does: the block's own, else the first matching rule that
  sets one, else the first `[[qbittorrent]]` and qBittorrent's own choice of folder. Each is decided on its own, so
  one rule can give the client and a later one the save path.
- **The top-level `languages` does not apply to tag rules.** A rule matches by language only if it sets
  `languages` itself; without it, the rule matches downloads in every language. Tag rules sort out what has
  already been selected, so an inherited language would leave a book that an autograb with `languages = []`
  selected in another language without its category, and without a library.

The result is applied when the torrent is added to qBittorrent. Each rule can also be applied to torrents
you already have, from the Config page; that sets the category and tags only, and doesn't move a torrent to
another qBittorrent or folder.

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
| `download_dir` | | Accept torrents whose save path is under this folder, matched by whole folder names: `/data/audio` does not take torrents saved in `/data/audiobooks`. |
| `name` | unset | Label for the library, used by [`[opds] libraries`](#opds) and the page filters. Two libraries can't have the same name (ignoring case). |
| `method` | global `link_method` | How files are placed. See below. |
| `allow_tags` | `[]` | If not empty, the torrent must have at least one of these tags. |
| `deny_tags` | `[]` | A torrent with any of these tags is rejected. |
| `audio_types` | global | Overrides the global format list for linking into this library. |
| `ebook_types` | global | Overrides the global format list for linking into this library. |
| `metadata_json` | global `metadata_json` | Write a `metadata.json`, Audiobookshelf's format, next to each book's files. |
| `metadata_opf` | global `metadata_opf` | Write a `metadata.opf`, Calibre's format, next to each book's files. Audiobookshelf reads it too. |
| `folder_names` | `"posix"` | Which characters folder names may hold. `"posix"` keeps names as they are, removing only `/`. `"portable"` makes them safe on Windows, SMB shares, exFAT and NTFS. See [folder naming](#folder-naming). |
| `folder_template` | unset | The folder layout under `library_dir`. Unset keeps the default layout. See [folder template](#folder-template). |
| `layout` | `"book"` | `"book"`: one folder per book, as `folder_template` says. `"series"`: one folder per series holding every book of the series, one file each, for comic servers such as Komga. See [series layout](#series-layout). |
| `oneshots_dir` | `"_oneshots"` | Series layout only: the folder under `library_dir` that books without a series go into. Komga's One-Shots directory setting must name the same folder. |
| `series_json` | `true` | Series layout only: write a Mylar `series.json` into each series folder. |
| `language_in_folder` | `"on_clash"` | When a book's folder name ends with its language, such as `Gamma (Greek)`. `"on_clash"`: only when its plain folder already holds the same title in another language, so two editions don't share a folder. `"other"`: every book not in the first of the top-level `languages`, which needs `languages` set. See [languages in folder names](#languages-in-folder-names). |

- **Exactly one of `category` or `download_dir`** must be set. Setting both or neither is an error.
- **Tags.** A torrent with any `deny_tags` tag is rejected. If `allow_tags` is not empty, the torrent
  must also have at least one of them.
- **`method`:**

| Value | Behaviour |
|---|---|
| `hardlink` | Hard link; fails if source and library are on different filesystems. |
| `hardlink_or_copy` (default) | Hard link, falling back to a copy when a hard link isn't possible there. |
| `hardlink_or_symlink` | Hard link, falling back to a symbolic link when a hard link isn't possible there. |
| `copy` | Copy the files. |
| `symlink` | Symbolic link. |
| `no_link` | Record metadata only; no files are placed. |

- **When the fallback is taken.** `hardlink_or_copy` and `hardlink_or_symlink` fall back only when a hard link
  can't be made there: the download is on another disk or filesystem (`EXDEV`), the filesystem has no hard links
  (`EPERM`, `EOPNOTSUPP`, `ENOSYS`), or the file has as many links as it may (`EMLINK`). Any other failure, such as a
  full disk (`ENOSPC`), a permission problem (`EACCES`), a read-only library (`EROFS`) or a missing download
  (`ENOENT`), would make the copy or the link fail too, so it is reported as itself: the Link error names it.
- From each torrent, only the files of its best audio format and its best ebook format are linked. The
  audio side uses `music_types` for Musicology, `radio_types` for Radio and `audio_types` for everything
  else; the ebook side uses `ebook_types`. A library's own `audio_types` replaces the audio-side list for
  every media type, Musicology and Radio included, and its own `ebook_types` replaces the ebook-side list.
- Files in subfolders named like `CD 1`, `Disc 2` or `Disk 3` go to a `Disc N` folder. Other folder
  structure is flattened.
- A `metadata.json` is written next to the files, for every media type, unless the library sets
  `metadata_json = false`. It is plain JSON in the format Audiobookshelf reads; see
  [`metadata.json`](user-guide.md#metadatajson) in the user guide for its fields.
- With `metadata_opf = true`, a `metadata.opf` is written as well: the OPF file Calibre keeps next to its
  books and imports from. See [`metadata.opf`](user-guide.md#metadataopf) in the user guide.
- Changing either setting affects books linked from then on. To apply it to books already in the library,
  relink them: tick them on the **Torrents** page and use **Relink selected**.
- A torrent without any author cannot be linked and is reported on the Errors page, except into a series library.

### Folder naming

Under `library_dir`, each book gets its own folder. By default:

- With series: `Author/Series/Series #<entries> - Title`.
- Without series: `Author/Title`.
- A series without a number gets its own folder, but no `Series # - ` before the title.
- The author is the first author.
- An edition adds `, <Edition>` to the last part.
- Narrators add ` {<first narrator>}` to the last part, unless `exclude_narrator_in_library_dir` is `true`.
  With that set, if the narrator-less folder already exists on disk for another book, the narrator form is used to
  avoid a collision.

Examples:

```
Brandon Sanderson/Stormlight Archive/Stormlight Archive #1 - The Way of Kings {Michael Kramer}
Andy Weir/Project Hail Mary {Ray Porter}
Terry Pratchett/Discworld/Discworld #1 - The Colour of Magic
```

**Characters.** With `folder_names = "posix"`, the default, only `/` and NUL are removed, which is all Linux and macOS
refuse. Names can then hold `:`, `?`, `"` and other characters Windows refuses, which show up mangled, or fail to be
written, on an SMB share, an exFAT or NTFS disk, or when synced to Windows or Android. `folder_names = "portable"`
avoids that:

- A colon becomes ` -`: `Dune: Part One` is `Dune - Part One`.
- `<`, `>`, `"`, `\`, `|`, `?`, `*` and control characters are removed.
- Trailing dots and spaces are removed.
- Names Windows reserves (`CON`, `PRN`, `AUX`, `NUL`, `COM1` to `COM9`, `LPT1` to `LPT9`, also with an extension) get
  a `_`: `CON_`.

Under both, a name left with no characters at all is `_`, so the book doesn't move up a level.

**Length.** Each folder name is at most 255 bytes, the most file systems allow. A longer one would fail to link. For
the last part, the title is shortened and ends in `…`; the series number, the edition and the narrator stay, as they
tell two copies apart. Other parts, and a last part too long even with the title gone, are cut at the end. The files
inside the folder keep the torrent's own names.

### Folder template

`folder_template` sets the layout itself. `/` separates the folders, and these tokens are filled in from the book's
metadata:

| Token | Value |
|---|---|
| `{author}` | The first author. |
| `{authors}` | All authors, joined with `, `. |
| `{series}` | The series name: the first series with a number, otherwise the first series. |
| `{series_number}` | The book's number in that series, as in the default layout: `1`, `1, 2`, `1-3`. |
| `{title}` | The title. Required, so two books by the same author don't share a folder. |
| `{edition}` | The edition, such as `2nd Edition`. |
| `{narrator}` | The first narrator. Empty when `exclude_narrator_in_library_dir` leaves it out. |
| `{language}` | The language, such as `English`. |
| `{year}` | The year the torrent was uploaded to MAM, the only date the metadata has. |

- A folder whose tokens are all empty is left out, the way a book without a series has no series folder.
- Text in square brackets is an optional group, written only when every token in it has a value: `[, {edition}]`
  adds `, 2nd Edition` for a book with an edition and nothing for one without. Groups can't be nested and can't hold
  a `/`.
- `{{`, `}}`, `[[` and `]]` are a literal brace or bracket.
- Folder names from a template are cleaned and shortened the same way.

The default layout written as a template:

```toml
folder_template = "{author}/{series}/[{series} #{series_number} - ]{title}[, {edition}][ {{{narrator}}}]"
```

A comics library ordered by series, still one folder per book (for Komga, use the [series layout](#series-layout)):

```toml
[[library]]
library_dir = "/data/comics"
category = "comics"
folder_names = "portable"
folder_template = "{series}/[{series_number} - ]{title} ({year})"
```

A template that can't be used stops startup with the reason: an unknown token, a `{` or `[` without its closing
`}` or `]`, an empty folder (`a//b`), a leading `/`, or no `{title}`.

### Languages in folder names

A book in two languages is two books, and two editions with the same title, such as *Gamma* in English and in Greek,
would get the same folder. `language_in_folder` keeps them apart by ending a folder name with the language:

- **`"on_clash"`**, the default: when the plain folder already holds a linked book in another language, the new one
  goes into `Gamma (Greek)`. The edition linked first keeps the plain folder, and no folder that is already right
  changes. A library with one language only never sees the mark.
- **`"other"`**: every book not in the first of the top-level `languages` gets the mark, whichever edition came first:
  with `languages = ["English", "Hungarian"]`, a Hungarian book goes into `Discworld #1 - A mágia színe (Hungarian)`.
  Switching to it flags the library's other-language books as *Different folder* until they are relinked.

Two editions that an earlier version linked into one folder are both flagged *Different folder*; relinking one of them
gives it a folder of its own. In a [series library](#series-layout), where every book of a series shares the series
folder, `"other"` marks the file name instead (`Saga #001 - Saga, Volume 1 (Hungarian).cbz`); two editions with the same
file name otherwise tell apart as `name (2).cbz`.

### Series layout

Comic servers read a library the other way round from Audiobookshelf. Komga makes a series of every folder that
holds files, at any depth, and a book of each file in it, so the book layout's one folder per book shows every
volume as a series of its own. `layout = "series"` links a library in the shape Komga reads:

```
comics/Saga/Saga #001 - Saga, Volume 1.cbz
comics/Saga/Saga #002 - Saga, Volume 2.cbz
comics/Saga/Saga #003 - Saga, Volume 3.cbz
comics/Saga/series.json
comics/_oneshots/Some Standalone Graphic Novel.cbz
```

```toml
[[library]]
name = "Comics"
category = "comics"
library_dir = "/data/comics"
layout = "series"
folder_names = "portable"
```

- **Folders.** One per series, named after it: the first series with a number, otherwise the first series. A book
  without a series goes into `oneshots_dir`. The author is left out: MAM lists writers and artists together, and
  writers change within a long series.
- **Files.** A torrent with one file of the chosen format is renamed `<Series> #<number> - <Title>[, <Edition>]`, or
  `<Series> - <Title>` without a number, keeping its extension; in the one-shots folder, `<Title>[, <Edition>]`.
  Numbers are padded to three digits so the names sort in series order: `1` is `001`, `1.5` is `001.5`, `1-6` is
  `001-006`, `2p1` is `002p1`. A torrent with several files, such as a pack, keeps its own file names in the series
  folder, as their numbering can't be told apart.
- **Another book's file at the name**, such as a second copy of a volume, makes this one `<name> (2).<ext>`.
- **Formats.** Only the ebook side is linked: comics, manga and other ebooks. A torrent with only audio files is a
  Link error: *This library's series layout links only ebook formats*.
- **No author needed.** A torrent without an author is linked.
- **`series.json`.** With `series_json`, the default, each series folder gets a Mylar `series.json` with the series'
  name as MAM has it, so a `:` that `folder_names = "portable"` took out of the folder name is back in Komga's title.
  Fetchshelf doesn't know the rest (publisher, status, issue count), so it writes those empty, and `Continuing` as the
  status; set `series_json = false`, or turn off Komga's *Import Mylar series.json* option, to keep what Komga reads
  from the books instead. The last book leaving a folder takes `series.json` with it.
- **No per-book files.** `metadata_json` and `metadata_opf` can't be set: a series folder holds many books. The
  top-level ones don't apply to a series library.
- **No `folder_template`.** The series layout names its folders and files itself.
- **Not for Audiobookshelf.** Audiobookshelf takes a folder for one item, so the Audiobookshelf job leaves a series
  library's books alone.
- Names are cleaned and shortened as the book layout's are; a file name leaves room for its extension.
- No `ComicInfo.xml` is written into the archives: with hardlinks, changing the archive changes the file being
  seeded.

### When folder names change

When metadata or config changes so a linked book's expected folder differs from where it is, the UI flags
it as a mismatch (*No library*, *Different library* or *Different folder*). **Refresh and relink** on the
torrent's page, or **Relink selected** on the Torrents page, links the book again from its download into
the library and folder it now belongs in. Changing `folder_names`, `folder_template` or
`exclude_narrator_in_library_dir` flags the books whose folder it changes as *Different folder* until they are
relinked. In a series library, a book's file name counts too: a corrected title or number flags it, and relinking
renames the file, leaving the series' other books alone. Switching a library's `layout` flags every book in it;
**Relink selected** moves them, and the book layout's folders go with their last file.

## `[[notify]]`

Optional and repeatable. Sends a message to a phone or a chat when something needs attention, such as MAM
refusing `mam_id` or a job that keeps failing, and, if asked, when a book is grabbed or linked. Each block is
one place to send to. Without any `[[notify]]` block, nothing is sent. See
[Getting notified](user-guide.md#getting-notified) in the user guide.

| Setting | Default | Description |
|---|---|---|
| `name` | `notify[i]` | A label for the log and the Config page. Must be unique among the blocks, ignoring case. |
| `url` | (required, or `url_file`) | Where the message is POSTed. Treated as a secret, since topics and webhook URLs often are one: shown only as set on the Config page and in `--check-config`, and left out of every log line and error message. Must be an `http` or `https` address. |
| `url_file` | unset | Full path of a file that holds the URL, instead of writing it in `url`. Works like [`mam_id_file`](#reading-mam_id-from-a-file). |
| `format` | `"json"` | The shape of the message: `json`, `ntfy`, `gotify`, `discord` or `apprise`. See [formats](#notification-formats). |
| `token` | unset | Sent as `Authorization: Bearer …`, for ntfy access tokens and an Apprise API server that asks for one. Gotify's application token goes in its URL (`?token=`), as Gotify documents. Treat it as a secret. |
| `events` | (required) | Which [events](#notification-events) to send: a list of event names, or `["all"]`. Must not be empty. |

At startup, and in [`--check-config`](#checking-a-configuration), each block is checked: an unknown event or
format, a `url` that is not an `http` or `https` address, both or neither of `url` and `url_file`, an empty
`events` list and a duplicate `name` all stop Fetchshelf from starting, with a message that names the block
but never repeats the URL.

A top-level `web_url`, such as `"https://fetchshelf.example.net"`, is the address messages link to: a grabbed
book links to its page, a failing job to its history. Without it, messages carry no links, since the address
a browser uses to reach Fetchshelf isn't known to the server. `FETCHSHELF_CONF_web_url` sets it too.

### Notification events

| Event | Sent when | Text, for example |
|---|---|---|
| `mam_refused` | MAM answers HTTP 403 (`mam_id` refused) or 406 (account blocked). Once per pause, not once per job. | "MAM refused mam_id (HTTP 403). MAM jobs are paused until 14:32 UTC. Replace mam_id and restart Fetchshelf." |
| `rate_limited` | MAM answers HTTP 429. At most once an hour. | "MAM asked Fetchshelf to slow down (HTTP 429); MAM calls are paused for a minute." |
| `job_failing` | A job's run fails after a run that didn't fail. | "Job Link failed: the connection to qBittorrent failed." |
| `job_recovered` | A job's run succeeds, or finishes with errors, after failed ones. | "Job Link works again, after 6 failed runs since 08:30 UTC." |
| `job_errors` | A run finishes with errors after a run that finished without them. Runs that were cancelled, or that Fetchshelf stopped during, don't count. | "Link: 3 torrents linked; 2 could not be linked; see the Errors page" |
| `grabbed` | A torrent is sent to qBittorrent. | "Grabbed The Way of Kings by Brandon Sanderson (Autograb Freeleech, freeleech)." |
| `linked` | A torrent is linked into a library. | "Linked The Way of Kings by Brandon Sanderson into Audiobooks." |
| `removed_from_mam` | [`mark_removed`](#mark_removed) finds that MAM no longer has a stored torrent. | "MAM no longer has The Way of Kings by Brandon Sanderson." |
| `hit_and_run` | MAM counts more hit-and-runs ("Not Seeding - H&R - Not Yet Satisfied") than the last count seen. | "MAM counts 1 hit-and-run (Not Seeding - H&R - Not Yet Satisfied). Seed it again to avoid a penalty." |
| `budget_spent` | The Download job leaves selections queued because the ratio budget or the unsatisfied-snatch slots are spent, after a run that left none. | "3 selections wait: the ratio budget left is 0 B, less than the 1.2 GiB the first of them needs (min_ratio 2)." |
| `bonus_spent` | The [Bonus job](#bonus) buys something with bonus points. One message per purchase. | "Bought 50 GB of upload credit with bonus points. 74,999 points are left, of 99,999." |
| `unsatisfied_not_seeding` | The [Seeding job](#seeding) finds that a torrent stopped seeding before it was satisfied. Merged like `grabbed` when several stop at once. | "The Way of Kings isn't seeding. Not seeding, and seeded 41 of 72 hours: seed it 31 more by 2026-12-12 to avoid a hit-and-run." |

- **Only changes are sent,** not states. A job that fails every 10 minutes for a day sends `job_failing`
  once and `job_recovered` once; selections that keep waiting for budget send `budget_spent` once. How many
  the last Download run left waiting is kept in the database, so neither a restart nor a run that failed, or
  that MAM stopped, in between sends it again.
- **One MAM refusal is one message.** When MAM refuses `mam_id`, every job that talks to MAM fails. A block
  that wants `mam_refused` gets that message and not a `job_failing` for each job it stopped; a block that
  doesn't want it still gets the `job_failing` messages. The same goes for a 429 and `rate_limited`.
- **Grabs and links are merged.** Those within 30 seconds of each other are sent as one message, "Grabbed 4
  torrents: A, B, C and 1 more.", so a Hardcover list run that selects 20 books sends one message, not 20. A
  burst that never pauses is sent after 5 minutes at the latest. Such a message links to the **Events** page,
  and in the `json` format its `fields.items` hold each torrent's own fields.
- **`hit_and_run`** compares each account read with the last count seen, which is kept in the database, so
  a hit-and-run that appears while Fetchshelf is down is still sent after the restart. A higher count is
  sent and stored; a lower one is only stored, so a later rise is noticed; the same count does nothing. With
  no stored count, it counts as 0: a hit-and-run that already exists is sent once, when a block first asks
  for the event. The count is only compared and stored while some block wants the event.
- **Delivery.** Messages are sent in the background, so a slow or unreachable service never holds a job up.
  Each block has a queue of its own, sent in order: a service that is down holds up only its own messages,
  never another block's. A failure, an HTTP 5xx or an HTTP 429 is tried once more after 10 seconds; then the
  message is dropped and logged as a warning. A refused token or a wrong address (HTTP 4xx) is not tried
  again. Delivery failures are not themselves notified. Requests time out after 10 seconds.
- **Not stored.** Messages waiting when Fetchshelf stops, the one being sent included, get 5 seconds to be
  sent; what is left then is lost, and the log says how many. The Events page and each job's history hold
  the same facts.
- **At most 200 messages wait** for each block. When more do, the oldest is dropped, with a line in the log.
- **Config page.** It lists each block with **Send a test message**, which sends "Fetchshelf test message
  from *host*" to that block straight away and shows the HTTP status or the error.

### Notification formats

| Format | What is sent |
|---|---|
| `json` | `{"event": "grabbed", "title": "…", "message": "…", "url": "…", "time": "…", "fields": {…}}`, with `fields` holding the event's data (MAM id, job name, counts) and `url` left out without `web_url`. For Home Assistant, n8n, Node-RED and custom scripts. |
| `ntfy` | The message as the body, with `Title`, `Tags` (`warning` for problems, `books` for grabs and links) and `Click` (the link) headers, and `Priority: high` for `mam_refused` and `job_failing`. A title that isn't plain ASCII is sent as an RFC 2047 encoded word, which ntfy decodes. |
| `gotify` | `{"title", "message", "priority"}`, with priority 8 for problems and 4 otherwise, and the link as `extras["client::notification"].click.url`. |
| `discord` | `{"content": null, "embeds": [{"title", "description", "url", "color"}]}`, red for problems, green for `job_recovered`, blue otherwise. |
| `apprise` | `{"title", "body", "type"}` for an [Apprise API](https://github.com/caronc/apprise-api) server, which forwards to any of Apprise's services. The link ends the body, and `type` is `failure`, `warning`, `success` or `info`. |

Problems are `mam_refused`, `rate_limited`, `job_failing`, `job_errors` and `hit_and_run`.

One block for each of the five targets:

```toml
# ntfy: any topic on ntfy.sh, or on a server of its own. A protected topic needs an access token.
[[notify]]
name = "Phone"
url = "https://ntfy.sh/fetchshelf-x7k2"        # the topic name is the secret: make it hard to guess
format = "ntfy"
token = "tk_replace-with-an-access-token"
events = ["mam_refused", "job_failing", "job_recovered", "grabbed"]

# Gotify: the application token goes in the URL.
[[notify]]
name = "Gotify"
url = "https://gotify.example.net/message?token=replace-with-an-application-token"
format = "gotify"
events = ["mam_refused", "job_failing", "hit_and_run"]

# Discord: a webhook, from Server Settings → Integrations → Webhooks.
[[notify]]
name = "Discord"
url = "https://discord.com/api/webhooks/123456789/replace-with-the-webhook-token"
format = "discord"
events = ["mam_refused", "job_failing", "job_recovered", "linked"]

# Apprise API: the stateless endpoint, with the key of a saved configuration.
[[notify]]
name = "Apprise"
url = "http://apprise:8000/notify/fetchshelf"
format = "apprise"
events = ["mam_refused", "job_failing", "budget_spent"]

# Anything that takes a JSON POST: Home Assistant, n8n, Node-RED, a script.
[[notify]]
name = "Home Assistant"
url = "http://homeassistant:8123/api/webhook/fetchshelf"
events = ["all"]
```

Keep the URLs out of a file that is shared by putting each in a file of its own with `url_file`, for example a
Docker secret.

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

Used by `languages`, at the top of the file and in filters. Names are case-insensitive and common aliases work (for example `Brazilian`,
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

# Search MAM for the books on the to-read shelf of a Goodreads export, kept next to config.toml.
[[file_list]]
name = "Goodreads"
path = "goodreads_library_export.csv"
shelf = "to-read"

[[file_list.grab]]
cost = "free"
languages = ["English"]
```
