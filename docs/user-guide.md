# Fetchshelf user guide

> **Work in progress.** This guide is incomplete and may change.

This guide is for people who run Fetchshelf. It assumes you use Docker, optionally with Docker Compose,
are comfortable editing a text file, and know how MaM works: ratio, unsatisfied snatches, freeleech, wedges
and VIP are not explained here. It does not cover building Fetchshelf from source.

## Contents

- [What is Fetchshelf?](#what-is-fetchshelf)
- [Before you start](#before-you-start)
- [Installing with Docker](#installing-with-docker)
- [Installing with Docker Compose](#installing-with-docker-compose)
- [First start](#first-start)
- [Configuring Fetchshelf](#configuring-fetchshelf)
- [Connecting to MaM](#connecting-to-mam)
- [Connecting to qBittorrent](#connecting-to-qbittorrent)
- [Setting up your library](#setting-up-your-library)
- [Connecting to Audiobookshelf](#connecting-to-audiobookshelf)
- [Finding and downloading books](#finding-and-downloading-books)
- [Automatic downloads](#automatic-downloads)
- [Importing reading lists](#importing-reading-lists)
- [Protecting your account](#protecting-your-account)
- [How finished downloads are organized](#how-finished-downloads-are-organized)
- [Using the web interface](#using-the-web-interface)
- [Background jobs](#background-jobs)
- [Common setups](#common-setups)
- [Updating Fetchshelf](#updating-fetchshelf)
- [Backups and moving to a new machine](#backups-and-moving-to-a-new-machine)
- [Security and privacy](#security-and-privacy)
- [Troubleshooting](#troubleshooting)
- [Frequently asked questions](#frequently-asked-questions)
- [Getting help](#getting-help)
- [Open items for this guide](#open-items-for-this-guide)

## What is Fetchshelf?

Fetchshelf is a self-hosted service that connects the private book tracker
[MyAnonamouse](https://www.myanonamouse.net) (MaM) with the BitTorrent client qBittorrent and, optionally,
[Audiobookshelf](https://www.audiobookshelf.org). It runs in the background, has a web interface for
looking at what it is doing, and is configured with a single text file.

It has two halves that you can use together or separately:

- **The auto-downloader** finds torrents on MaM that match rules you write (your bookmarks, a favourite
  author, a Notion reading list, and so on) and adds them to qBittorrent. While doing so it protects your
  account: it keeps your ratio above a limit, leaves some unsatisfied-snatch slots free and holds back a
  number of freeleech wedges.
- **The library organizer** watches qBittorrent for finished torrents and links the book files into a tidy
  `Author/Series/Title` folder tree, with a `metadata.json` that Audiobookshelf can read. When a better
  format of a book arrives, it retires the older copy.

If you only want a tidy library, skip the auto-downloader. If you only want automatic downloads, skip the
organizer.

Fetchshelf is **not** a torrent client (qBittorrent does the downloading), not a media player
(Audiobookshelf or similar does the reading and listening) and has no user accounts of its own. See
[Security and privacy](#security-and-privacy).

## Before you start

You need:

- **A MaM account in good standing.** Fetchshelf acts on your behalf. You remain responsible for following
  MaM's rules, including ratio, seeding and unsatisfied-snatch requirements. Start with conservative
  settings (see [Protecting your account](#protecting-your-account)).
- **qBittorrent** with its Web UI enabled, reachable from the machine that runs Fetchshelf. MaM only allows
  certain client versions and changes the list over time, so check MaM's current list of approved clients
  before you install or upgrade qBittorrent.
- **Docker**, and optionally Docker Compose.
- **Audiobookshelf**, optional.
- **Disk layout that allows hardlinks**, if you want files to appear in the library without using extra
  space. Hardlinks only work when the download folder and the library folder are on the **same
  filesystem**. Otherwise use the copy or symlink link methods (see
  [Setting up your library](#setting-up-your-library)).

## Installing with Docker

The image is published to the GitHub Container Registry as `ghcr.io/crimsonking60/fetchshelf`, built for
64-bit x86 (`linux/amd64`), 64-bit ARM (`linux/arm64`, for example a Raspberry Pi 4 or 5 or most ARM-based
NAS devices) and 32-bit ARM (`linux/arm/v7`, for example a Raspberry Pi with a 32-bit operating system).
Docker picks the right one for your machine automatically. These tags are available:

| Tag | What it is |
|---|---|
| `latest` | The newest release. The examples in this guide use it. |
| A version, such as `2026.10.2` | One specific release. It never changes, so use it to upgrade only when you choose to. |
| `develop` | The current development build, for testing only. It can change several times a day and can break your setup. |

Versions are written `YYYY.MM.N`: the year and month of the release, then a number that counts releases
within that month from `0`. Every release and what changed in it is listed in
[RELEASES.md](../RELEASES.md).

1. Create folders on the host for Fetchshelf's configuration and database:

   ```sh
   mkdir -p /srv/fetchshelf/config /srv/fetchshelf/data
   ```

2. Start the container. This example also mounts a shared media folder, which holds the downloads and
   the library (see [Setting up your library](#setting-up-your-library)):

   ```sh
   docker run -d \
     --name fetchshelf \
     --restart unless-stopped \
     -p 3157:3157 \
     -e PUID=1000 \
     -e PGID=1000 \
     -v /srv/fetchshelf/config:/config \
     -v /srv/fetchshelf/data:/data \
     -v /srv/media:/media \
     ghcr.io/crimsonking60/fetchshelf:latest
   ```

3. Check that it is running:

   ```sh
   docker logs fetchshelf
   ```

   Then open `http://<your-host>:3157` in a browser.

   About a minute after starting, `docker ps` shows the container as `(healthy)`. See
   [Health check](#health-check).

What the options mean:

| Option | Meaning |
|---|---|
| `-p 3157:3157` | The web interface listens on port 3157 inside the container. Change the left number to use another port on the host. |
| `/config` volume | Holds `config.toml`. |
| `/data` volume | Holds the database (`data.db`), where Fetchshelf remembers torrents, selections and events. |
| `/media` volume | Not required by Fetchshelf itself. A folder that holds both your downloads and your library, so linking can work. Use the same host folder, mounted at the same path, in qBittorrent and Audiobookshelf. |
| `PUID` / `PGID` | The user and group id that Fetchshelf runs as inside the container. Set them to the owner of your config, data and media folders (run `id` on the host to see yours) so files are created with the right owner. Both default to `1000`. |

The container starts as root only to adjust ownership of `/config` and `/data`, then runs Fetchshelf as the
`PUID`/`PGID` user. If you start the container with a fixed user instead (`--user` or `user:`), `PUID` and
`PGID` are ignored and you must make sure that user can write to `/config` and `/data`.

Other environment variables you can set:

| Variable | Default | Meaning |
|---|---|---|
| `FETCHSHELF_CONFIG_FILE` | `/config/config.toml` | Where the configuration file is. |
| `FETCHSHELF_DATA_FILE` | `/data/data.db` | Where the database is. |
| `FETCHSHELF_LOG` | `debug` | Log level: `verbose`, `debug`, `information`, `warning` or `error`. |
| `FETCHSHELF_LOG_DIR` | empty | If set, also write daily rotating log files to this folder. Logs always go to the container output. |
| `FETCHSHELF_CONF_<setting>` | | Override a configuration setting. See [Configuring Fetchshelf](#configuring-fetchshelf). |

### Health check

The image has a built-in health check. Every 30 seconds Docker asks Fetchshelf whether it is running and can
open its database, and `docker ps` shows the container as `healthy` or `unhealthy`. The check follows
`web_port`, so it keeps working if you change the port. It does not check MaM, qBittorrent or
Audiobookshelf: problems with those show up on the **Jobs** and **Errors** pages instead.

The same check is available at `http://<your-host>:3157/health` for monitoring tools and reverse proxies.
It answers `Healthy` with status 200, or `Unhealthy` with status 503. Docker itself does not restart an
unhealthy container; use the health status for monitoring, or with a tool that acts on it.

## Installing with Docker Compose

Compose keeps all of this in one file. Save this as `docker-compose.yaml`:

```yaml
services:
  fetchshelf:
    image: ghcr.io/crimsonking60/fetchshelf:latest
    container_name: fetchshelf
    restart: unless-stopped
    ports:
      - "3157:3157"
    environment:
      PUID: "1000"
      PGID: "1000"
    volumes:
      - ./fetchshelf/config:/config
      - ./fetchshelf/data:/data
      - /srv/media:/media
```

Common commands, run in the folder that contains the file:

| Command | What it does |
|---|---|
| `docker compose up -d` | Create and start the container. |
| `docker compose logs -f fetchshelf` | Follow the log. |
| `docker compose restart fetchshelf` | Restart, for example after editing `config.toml`. |
| `docker compose stop` / `docker compose down` | Stop, or stop and remove the container. Your config and data stay in the mounted folders. |

### Together with qBittorrent and Audiobookshelf

If you run all three in one Compose project, mount your media folder at `/media` in every container. Then
the path that qBittorrent reports for a download is also valid inside Fetchshelf, so no `path_mapping` is
needed, hardlinks work because downloads and library share one filesystem, and Audiobookshelf sees each
book at the same path as Fetchshelf, which it needs to match them.

```yaml
services:
  qbittorrent:
    # Use a version tag from MaM's list of approved clients instead of latest.
    image: lscr.io/linuxserver/qbittorrent:latest
    container_name: qbittorrent
    restart: unless-stopped
    environment:
      PUID: "1000"
      PGID: "1000"
      WEBUI_PORT: "8080"
    ports:
      - "8080:8080"                # keep both numbers equal to WEBUI_PORT
    volumes:
      - ./qbittorrent:/config
      - /srv/media:/media

  audiobookshelf:
    image: ghcr.io/advplyr/audiobookshelf:latest
    container_name: audiobookshelf
    restart: unless-stopped
    ports:
      - "13378:80"
    volumes:
      - ./audiobookshelf/config:/config
      - ./audiobookshelf/metadata:/metadata
      - /srv/media/library:/media/library

  fetchshelf:
    image: ghcr.io/crimsonking60/fetchshelf:latest
    container_name: fetchshelf
    restart: unless-stopped
    depends_on:
      - qbittorrent
    ports:
      - "3157:3157"
    environment:
      PUID: "1000"
      PGID: "1000"
    volumes:
      - ./fetchshelf/config:/config
      - ./fetchshelf/data:/data
      - /srv/media:/media
```

With this layout, `config.toml` can address qBittorrent by service name, `url = "http://qbittorrent:8080"`.
For Audiobookshelf, use the address you open it at in a browser, such as `http://192.168.1.10:13378`, so
that the links Fetchshelf shows to Audiobookshelf also work in your browser (see
[Connecting to Audiobookshelf](#connecting-to-audiobookshelf)). The library folders are
`/media/library/audiobooks` and `/media/library/ebooks`, and the Audiobookshelf libraries use the same
folders (see [Connecting to Audiobookshelf](#connecting-to-audiobookshelf)).

After the first `docker compose up -d`, set up qBittorrent before you connect Fetchshelf to it:

1. Find the temporary Web UI password in `docker compose logs qbittorrent`. The user name is `admin`.
   qBittorrent makes a new temporary password on every start until you set your own.
2. Open qBittorrent at `http://<your-host>:8080` and sign in.
3. In **Options → Web UI**, set your own password. Fetchshelf needs a password that does not change.
4. In **Options → Downloads**, set **Default Save Path** to `/media/torrents`. Fetchshelf does not choose
   where downloads are saved, so without this they end up in `/downloads`, outside `/media`, where Fetchshelf
   cannot see them.

If port 8080 is already taken on your host, change `WEBUI_PORT` and both numbers in the port mapping
together, for example `WEBUI_PORT: "8090"` and `"8090:8090"`, and use `http://qbittorrent:8090` in
`config.toml`. qBittorrent refuses requests that arrive on a different port from its own.

## First start

On the first start, if there is no configuration file, Fetchshelf creates `config.toml` in your config
folder containing only an empty `mam_id`. The web interface starts, but **no background job runs** until
you set `mam_id`. That is intentional: you can look around before anything happens on your account.

1. Open `http://<your-host>:3157`.
2. The top bar has the pages **Search**, **Jobs**, **Selected**, **Torrents**, **Events**, **Errors**,
   **Duplicates** and **Config**. There is also a theme selector. See
   [Using the web interface](#using-the-web-interface).
3. Open **Config** to see the settings Fetchshelf loaded. It is read-only, and secrets are hidden.
4. Edit `config.toml` in the config folder you mounted, then restart the container.

When it is working, the **Jobs** page lists your jobs with their last run time and result, and the
container log shows each job starting and finishing.

## Configuring Fetchshelf

All settings live in one TOML file, `config.toml`, in the folder mounted at `/config`.

- **Where to start.** Pick the closest of the [numbered examples](config-examples/README.md), copy it to
  `config.toml` and replace the `replace-with-...` placeholders. Example 1 is the smallest, example 6
  shows every option.
- **Reference.** Every setting is described in the [configuration reference](configuration.md).
- **Mistakes are caught early.** Fetchshelf refuses to start with a setting it does not know, a missing
  required value, or an unrecognized name (for example a misspelled language). The error message names the
  problem. Check `docker logs fetchshelf`.
- **Changes need a restart.** Fetchshelf reads the file when it starts. After editing it, restart the
  container.
- **Environment overrides.** Any simple top-level setting can be set through an environment variable named
  `FETCHSHELF_CONF_<setting>` with the setting name in lower case, for example
  `FETCHSHELF_CONF_mam_id`. Environment variables win over the file. This is useful for keeping a secret such
  as `mam_id` out of a file you share. Sections such as `[[qbittorrent]]` cannot be overridden this way.
- **Intervals** are in minutes. `0` means "never on a schedule, only when I press **Run now**".

## Connecting to MaM

Fetchshelf logs in to MaM with a session id, the value of a session cookie that you create yourself.

1. Sign in to MaM and open your security preferences.
2. Create a new session. MaM ties a session to an address, so choose how it is locked to match where
   Fetchshelf runs. If your home IP address changes, pick the option that tolerates that.
3. Copy the session id and put it in `config.toml`:

   ```toml
   mam_id = "your-session-id"
   ```

4. Restart the container. The jobs now start.

For more about sessions and how MaM locks them, see MaM's own documentation.

Things to know:

- **Treat `mam_id` like a password.** Anyone who has it can act as you on MaM.
- **MaM rotates the cookie.** Fetchshelf stores the newest one it receives and prefers it on the next
  start, falling back to the one in your file if the stored one stops working.
- **Rate limits.** If MaM answers "too many requests", the job fails with an error and tries again at its
  next interval.
- **Blocked account.** If MaM temporarily blocks the account, any running download aborts. Wait, then
  check the account on MaM before running jobs again.
- **Expired or invalid session.** If jobs fail with an authentication error, create a new session and
  update `mam_id`.

## Connecting to qBittorrent

1. In qBittorrent, enable the Web UI (**Options → Web UI**) and note the address, user name and password.
2. Add the connection to `config.toml`:

   ```toml
   [[qbittorrent]]
   url = "http://qbittorrent:8080"
   username = "admin"
   password = "your-password"
   ```

3. Restart Fetchshelf and look at the log or the **Torrents** page. The **Check qBittorrent** button on
   the Torrents page asks qBittorrent about every torrent Fetchshelf knows and shows how many were found.

New downloads go to the **first** `[[qbittorrent]]` entry. Linking runs against every entry, so you can
list a second client for torrents that finish elsewhere.

### Paths: `path_mapping`

qBittorrent reports where it saved a download as a path **on its own machine or container**. Fetchshelf
needs the same location as **it** sees it. If both see the same path (for example both mount `/media`),
you need nothing. If they differ, map one to the other:

```toml
[qbittorrent.path_mapping]
"/downloads" = "/media/torrents"
```

This reads: where qBittorrent says `/downloads`, Fetchshelf should use `/media/torrents`. The longest
matching prefix wins.

Fetchshelf does not tell qBittorrent where to save a download, so the **Default Save Path** in
qBittorrent's **Options → Downloads** decides it. Make sure that folder is one Fetchshelf can reach, directly
or through `path_mapping`. A wrong or missing mapping is the most common cause of linking errors (see
[Troubleshooting](#troubleshooting)).

### Categories and tags

Fetchshelf puts a **category** and **tags** on each torrent it adds, based on your `[[tag]]` rules (see
[Setting up your library](#setting-up-your-library)). It creates a category in qBittorrent if it does not
exist yet.

### `on_cleaned`

When a better format of a book is linked, the older copy is retired. `on_cleaned` says what to do to the
old torrent in qBittorrent, such as moving it to a category or adding a tag, so you can find it and decide
what to do with it:

```toml
[qbittorrent.on_cleaned]
tags = ["superseded"]
```

Fetchshelf never deletes the downloaded data itself in this step. It removes only the library files it
linked.

## Setting up your library

A `[[library]]` tells Fetchshelf where finished books go. For each finished torrent the **first** library,
in file order, that accepts it is used. A library accepts a torrent in one of two ways, and you must choose
exactly one:

- by **category**: the torrent has that qBittorrent category; or
- by **download folder**: the torrent was saved under that folder.

```toml
[[library]]
name = "Audiobooks"
category = "audiobooks"
library_dir = "/media/library/audiobooks"

[[library]]
name = "Ebooks"
category = "ebooks"
library_dir = "/media/library/ebooks"
```

Categories come from `[[tag]]` rules, which also work for torrents you add by hand:

```toml
[[tag]]
categories = { audio = false, ebook = true }
category = "ebooks"

[[tag]]
categories = { audio = true, ebook = false }
category = "audiobooks"
```

The first matching rule that sets a category decides the category. Tags from every matching rule are
added. An `[[autograb]]` can also force a category with its own `category` setting.

You can narrow a library with `allow_tags` (the torrent needs at least one) and `deny_tags` (the torrent
is rejected if it has any), and give it its own `audio_types` / `ebook_types` to control which files are
linked into it.

### Link methods

`method` controls how a file gets from the download folder to the library:

| Method | Behaviour | When to use it |
|---|---|---|
| `hardlink` (default) | A second name for the same file. No extra space. Needs the same filesystem. | Downloads and library share a filesystem and you keep seeding. |
| `hardlink_or_copy` | Hardlink, falling back to a copy. | You are not sure the folders are on the same filesystem. |
| `hardlink_or_symlink` | Hardlink, falling back to a symbolic link. | As above, but you prefer links to copies. |
| `copy` | A full copy. | The library is on a different disk. Uses extra space. |
| `symlink` | A symbolic link to the download. | Rarely. Some programs cannot follow links. |
| `no_link` | Records metadata only; no files are placed. | You only want tracking. |

Seeding keeps working with hardlinks and symlinks, because the original files stay where qBittorrent put
them.

## Connecting to Audiobookshelf

This is optional. Without it, Audiobookshelf still reads the `metadata.json` file that Fetchshelf writes
next to every book. With it, Fetchshelf also:

- finds each linked book in Audiobookshelf and stores the link, shown on the torrent's page;
- pushes updated metadata to Audiobookshelf after linking or a refresh;
- deletes the old Audiobookshelf item when a book is replaced by a better format.

1. In Audiobookshelf, open **Settings → API Keys** and create a key for a user that can see your libraries.
2. Add the section:

   ```toml
   [audiobookshelf]
   url = "http://192.168.1.10:13378"
   token = "your-api-key"
   interval = 10
   ```

   Set `url` to the address you open Audiobookshelf at in a browser, using the host's IP address or name,
   not `localhost`. Fetchshelf uses it both to talk to Audiobookshelf and for the **Open in
   Audiobookshelf** link on a torrent's page, so it must work from the Fetchshelf container and from your
   browser. A Compose service name such as `http://audiobookshelf:80` works for matching, but the link
   then only works in a browser that can resolve that name, which is usually not the case outside Docker.

3. Make Audiobookshelf see your library at the **same path** as Fetchshelf. Fetchshelf finds a book in
   Audiobookshelf only if one of the Audiobookshelf library's folders contains the exact path where
   Fetchshelf linked the book. If Fetchshelf's `library_dir` is `/media/library/audiobooks`, mount the same
   folder at `/media/library/audiobooks` in the Audiobookshelf container and add that folder to the
   Audiobookshelf library. If the paths differ, for example `/audiobooks` in Audiobookshelf, no book is
   ever matched.

`interval` is how many minutes pass between matching runs. Remove the whole section to switch the
integration off.

## Finding and downloading books

The **Search** page is also the home page. Use it to search MaM yourself and download a book by hand.

- **Search** looks for your text in the title and author of every torrent on MaM. With the box empty, the
  page shows the newest torrents on MaM instead. Either way you get at most the 100 newest matches; there
  are no further pages, so make the search more specific if what you want is not there.
- **Hide downloaded torrents** hides every result that has a status (see below), so only torrents Fetchshelf
  has not handled yet remain.
- **Show** limits the list to one kind of torrent:

  | Choice | Shows |
  |---|---|
  | All | Every result. |
  | Ebooks | Ebooks and periodical ebooks. |
  | Audiobooks | Audiobooks and periodical audiobooks. |
  | Comics | Manga, and comic books and graphic novels. |
  | Other | Everything else, such as musicology and radio. |

  These two filters only hide rows in the results already on the page. They do not change what is asked
  of MaM, so they cannot bring in more results, and they are reset when you run a new search.
- Each result shows its title, authors, media type, size and status. The status says where the torrent is
  in Fetchshelf: **Selected** (waiting to be downloaded), **Downloaded** (in qBittorrent, not yet in your
  library) or **Linked** (in your library). It is empty for torrents Fetchshelf has not handled. It does not
  show torrents you snatched outside Fetchshelf.
- The **Cost** column marks **FL** (freeleech), **Personal FL** (personal freeleech) and **VIP** torrents.
- **Download** adds the torrent as it is. **Wedge download** spends a freeleech wedge so the torrent
  costs no ratio. A message at the top of the page says whether the torrent was selected, and if not, why:
  for example, that a copy is already in your library, or that none of its file types is in your
  preferred types.
- If you set `wedge_over` in the `[search]` section, results larger than that size have the
  **Wedge download** button highlighted:

  ```toml
  [search]
  wedge_over = "100 MiB"
  ```

A manual download goes through the same steps as an automatic one. It becomes a **selection**, which the
downloader processes with the ratio, unsatisfied-slot and wedge checks (see
[Protecting your account](#protecting-your-account)) and then adds to qBittorrent with the category and tags
from your `[[tag]]` rules. You can follow it on the **Selected** page.

## Automatic downloads

An `[[autograb]]` block is a search that Fetchshelf runs on a schedule. Everything that matches, and that
you can afford, is selected and downloaded.

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
```

### What to search: `type`

| `type` | Searches |
|---|---|
| `"bookmarks"` | Your MaM bookmarks. |
| `"freeleech"` | MaM's global freeleech list. |
| `"new"` (or `"torrents"`) | All torrents on MaM, newest first. |
| `"mine"` | Your own uploads. |
| `{ uploader = 12345 }` | One uploader's torrents. |

`type` is required. The name `new` is misleading: it searches **all** torrents, and "new" only describes the
default ordering. Each run reads the newest 100 matches (one page), so it keeps up with new uploads but does
not go back through the whole catalogue unless you raise `max_pages`.

### Filtering

Every autograb can use the same filter fields: media type, categories, languages, flags such as `explicit`
or `abridged`, file size, uploader, upload date, seeders, leechers, times snatched, and `max_authors` (which
skips anthologies, keeping a book like Good Omens with two authors). A torrent must satisfy **every** field
you set. The details are in the [configuration reference](configuration.md#the-shared-torrent-filter).

### What to pay: `cost`

| `cost` | Meaning |
|---|---|
| `free` (default) | Only freeleech or VIP torrents. |
| `ratio` or `all` | Pay with ratio. |
| `wedge` | Always use a wedge on torrents that are not already free. |
| `try_wedge` | Use a wedge if one is available above your reserve, otherwise pay with ratio. |
| `metadata_only` | Never download; just record the book's details. |

Torrents that are already free are always treated as free, so wedges are never wasted.

### Using several blocks together

Blocks are independent, so a common pattern is one block per cost level:

```toml
[[autograb]]            # take everything that is free
type = "bookmarks"
cost = "free"

[[autograb]]            # small files cost little ratio
type = "bookmarks"
cost = "all"
max_size = "30 MiB"

[[autograb]]            # use wedges for the rest
type = "bookmarks"
cost = "try_wedge"
```

### Trying a block out safely

- `dry_run = true` runs the whole search and selection but downloads and records nothing. It only logs what
  it would do. Look at the log, then remove it. Your ratio, unsatisfied-snatch slots and wedges are only
  checked when a download starts, so "would be selected" means it would be queued, not necessarily started.
- `search_interval = 0` stops the block running by itself. Start it from the **Jobs** page with **Run now**.
  Use both for a one-off backfill.
- `max_active_downloads` (which needs a `name`) caps how many unfinished downloads one block may have.

Examples of each pattern are in [`4-targeted-searches.toml`](config-examples/4-targeted-searches.toml).

## Importing reading lists

Instead of searching, you can keep a list of the torrents you want and let Fetchshelf fetch them.

### Notion

A Notion database whose rows contain links to MaM torrents:

```toml
[[notion_list]]
name = "Notion wishlist"
data_source = "00000000-0000-0000-0000-000000000000"
token = "your-notion-integration-token"
mam_fields = ["Audiobook link", "Ebook link"]

[[notion_list.grab]]
cost = "all"
languages = ["English"]
max_size = "15 MiB"

[[notion_list.grab]]
cost = "wedge"
languages = ["English"]
```

- `mam_fields` names the URL properties that hold MaM links. The number at the end of each link is the MaM
  torrent id. Ids Fetchshelf already knows are skipped.
- `grab` blocks decide what is acceptable and how to pay. For each linked torrent they are tried **in
  order, and the first block whose filter matches decides the cost**, so put the most specific one first.
  Every list needs at least one.
- Books you already have, in an equal or better format, are not downloaded again.

### Snatchlists

A `[[snatchlist]]` imports one of your MaM snatch lists as records of what you already own, without
downloading anything. This stops Fetchshelf downloading a book you got some other way. See
[`4-targeted-searches.toml`](config-examples/4-targeted-searches.toml).

## Protecting your account

Fetchshelf will not start a download that would break these limits.

| Setting | Default | What it protects |
|---|---|---|
| `min_ratio` | `2` | Your upload/download ratio. A download that would push you below it is skipped. |
| `unsat_buffer` | `10` | Unsatisfied-snatch slots. MaM limits how many torrents you may have snatched but not yet fully seeded. Fetchshelf always leaves this many slots free. |
| `wedge_buffer` | `0` | Freeleech wedges. This many are never spent automatically. |

Each autograb and each list can override `unsat_buffer` and `wedge_buffer` for itself.

How a run works out how much it may download:

- **Slots:** your unsatisfied limit, minus the torrents already unsatisfied, minus the buffer. That is the
  most torrents one run may select.
- **Ratio:** free torrents cost nothing. For the others, the size must fit within
  `(uploaded − downloaded − data still being downloaded) / min_ratio`.
- **Wedges:** a wedge is used only if more than `wedge_buffer` remain.

When a limit is reached, the remaining torrents stay selected on the **Selected** page and are tried on the
next run.

Other safeguards:

- `max_active_downloads` caps unfinished downloads per autograb.
- `ignore_torrents = ["123456"]` lists MaM ids that must never be selected.
- `add_torrents_stopped = true` adds torrents to qBittorrent in the stopped state, so you can review them
  before they start.
- Fetchshelf only removes files it linked itself, and never overwrites another torrent's files.

Suggested starting point for a new setup: `min_ratio = 2`, `unsat_buffer = 10` or more,
`wedge_buffer = 5`, `cost = "free"` on your first autograb, and `dry_run = true` until the log looks right.

## How finished downloads are organized

Every `link_interval` minutes (default 10), and after each download run, Fetchshelf looks for torrents in
qBittorrent that are 100% complete, finds the matching library and links the wanted files in.

- **Which files.** Only files with an extension in your `audio_types` / `ebook_types` lists are linked.
  Other files are left alone.
- **Folder names.** Under `library_dir`:

  | Book | Folder |
  |---|---|
  | In a series | `Author/Series/Series #1 - Title` |
  | Not in a series | `Author/Title` |
  | Audiobook with a narrator | the above plus ` {Narrator}` |
  | With an edition | the above plus `, 2nd Edition` |

  For example `Brandon Sanderson/Stormlight Archive/Stormlight Archive #1 - The Way of Kings {Michael Kramer}`.
  Set `exclude_narrator_in_library_dir = true` to leave the narrator out. The author is the first author.
  A torrent with no author cannot be linked and shows up on the **Errors** page.
- **Multi-disc audiobooks.** Files in sub-folders named like `CD 1`, `Disc 2` or `Disk 3` go into a
  `Disc N` folder. Other folder structure is flattened.
- **`metadata.json`.** Written next to the files, with authors, narrators, series, title, description,
  language and more, in the format Audiobookshelf reads.

### Better formats replace worse ones

Your `audio_types` and `ebook_types` lists are in order of preference. If two copies of the same book are
linked, the one whose format comes earlier in the list is kept, and ties go to the larger one. With
`ebook_types = ["epub", "pdf"]`, an epub replaces a pdf of the same book.

The replaced copy has its linked files, its folder (if empty), `cover.jpg` and `metadata.json` removed, is
tagged or moved in qBittorrent according to `on_cleaned`, and is shown as *replaced*. The downloaded data
itself is not deleted. You can still relink a replaced torrent later.

### When the folder no longer matches

If a book's metadata changes, or you change the configuration, the folder where it should be can differ
from where it is. The torrent is then flagged on the **Torrents** page as *No library*, *New library dir* or
*New path*. Open it and choose **Refresh and relink** to move the files.

## Using the web interface

There is no login. See [Security and privacy](#security-and-privacy).

| Page | What it is for |
|---|---|
| **Search** (home) | Search MaM and download books by hand. |
| **Jobs** | Every background job, its last run and result, when it runs next, and a **Run now** button. Updates live. Below them, the kinds of job you have not configured. |
| **Selected** | The queue of torrents that are chosen but not yet started. |
| **Torrents** | Every torrent Fetchshelf knows. |
| **Events** | A log of what happened: grabbed, linked, cleaned, updated. |
| **Errors** | Problems found by the Download, Link and Clean jobs. |
| **Duplicates** | Torrents skipped because you already have or have selected the same book. |
| **Config** | A read-only view of the active configuration, with secrets hidden. |

### Selected

Shows each queued torrent with its cost, category, tags, what chose it (an autograb, a list or a manual
search), and when it was queued and started. You can remove entries, or change a queued torrent's cost,
category and tags before it starts. The cost can be changed to `Ratio`, `TryWedge` or `UseWedge`; the free
costs only record what MaM reported, so they cannot be chosen by hand.

### Torrents

- **Search, sort and filter.** Search by title, author or series. The newest torrents come first; click
  the **Title** heading to sort by title, and click it again to reverse the order. Click a torrent's media
  type or language to show only torrents of that kind. Searching, sorting and filtering combine; the
  filters in use are listed above the table, each with a **show all** link to remove it. Pages hold 25
  torrents. Use **Previous** and **Next** below the list.
- **Columns.** Besides the metadata, *Client status* is the torrent's state in qBittorrent as of the last
  check, and *Mismatch* shows when a linked book's folder no longer matches your library settings (see
  [When the folder no longer matches](#when-the-folder-no-longer-matches)). **Edit** on each row opens the
  metadata editor.
- **Bulk actions.** Tick rows, then **Clean selected**, **Refresh selected** or **Remove selected**.
  *Clean* retires a torrent's library files, *Refresh* re-reads its metadata from MaM, and *Remove* deletes
  Fetchshelf's record (not the files).
- **Check qBittorrent.** Updates the *Client status* column for every torrent.
- **Torrent page.** Click a title for the full details: authors, narrators, series, language, media type,
  where the metadata came from, the library folder and linked files, the qBittorrent state, links to the
  torrent on MaM and, once the book is matched there, in Audiobookshelf, and the torrent's events. Notices
  show a library mismatch and, for a replaced book, a link to the torrent that replaced it. Actions:
  **Clean**, **Refresh**, **Refresh and relink**, **Start** and **Stop** in qBittorrent, **Remove from
  qBittorrent with data**, **Remove record**, and, for a replaced book, **Clear replaced marker**. While
  qBittorrent has the torrent, you can also change its category and tags there: untick a tag to remove it,
  type new ones separated by commas, and press **Set category and tags**.
- **Edit.** If MaM's metadata is wrong, **Edit** lets you correct the title, authors, narrators, series
  (one `Name #number` per line), language, category and flags. A hand-edited torrent keeps your values
  until you explicitly refresh it.

### Events

An audit log, newest first. Each entry says what happened to which torrent and, for metadata changes, which
fields changed. Events are never edited, so you can use them to see why something happened. Filter the list
by event type, by the autograb or list that grabbed a torrent, by the library that linked it, or by a
changed field.

### Errors

One entry per torrent and job, showing the job that ran into the problem (Download, Link or Clean), the
torrent, the error and when it last failed. The Link job tries a failed torrent again on every run: the
entry is updated while it keeps failing and disappears once it links. A queued download whose MaM link
went stale is taken out of the queue instead; **Retry download** puts it back on the **Selected** page.
Tick the entries you have dealt with and use **Remove selected**.

### Duplicates

Torrents that an autograb, list or manual download skipped because you already have, or have already
selected, the same book in an equal or better format. A selection that was dropped for a better copy is
listed here too. Each entry links to the skipped torrent on MaM and shows what it duplicates: the torrent's
page when Fetchshelf has it, otherwise the queued selection or the torrent on MaM. Tick the entries you no
longer need and use **Remove selected**.

### Config

Shows the settings in effect. For each autograb there is a **Run now** button that runs that block once.
For each `[[tag]]` rule there is **Apply to existing torrents**, which sets the rule's category and tags on
torrents you already have.

## Background jobs

Fetchshelf does its work in jobs, listed on the **Jobs** page.

| Job | What it does | Runs |
|---|---|---|
| Autograb (one per `[[autograb]]`) | Searches MaM and selects what qualifies. | Every `search_interval` minutes, or the block's own interval. |
| Snatchlist (one per `[[snatchlist]]`) | Imports your snatch list as owned books. | Same. |
| Notion list (one per `[[notion_list]]`) | Looks up each linked torrent and selects what qualifies. | Every `import_interval` minutes, or the list's own interval. |
| Download | Starts selected torrents in qBittorrent within your limits. | After autograbs and list runs select something, and every `search_interval` minutes. |
| Link | Links finished torrents into libraries. | Every `link_interval` minutes, and after each download run. |
| Clean | Retires lesser formats. | After each link run. |
| AbsMatcher | Matches linked books to Audiobookshelf items. | Every `[audiobookshelf]` `interval` minutes. |

Autograb, snatchlist and list jobs are named after the block's `name`, or `autograb[0]`, `snatchlist[1]`
and so on if it has none. Give your blocks names so the Jobs page is easy to read. Below the jobs, the page
lists each kind of optional job you have not configured, such as *Snatchlist is not configured*.

- **Run now** starts a job immediately. This works for every job, whatever its interval.
- An interval of `0` means the job never runs by itself.
- Only one job talks to MaM at a time. If you press **Run now** while another is running, it waits its
  turn.
- The *last result* shows success or the error. A failed job tries again at its next interval. One failing
  torrent never stops the others.
- With an empty `mam_id`, no job runs at all.

## Common setups

The [numbered examples](config-examples/README.md) are complete configs for these.

| I want to... | Start with | Notes |
|---|---|---|
| Keep my own downloads tidy, nothing automatic | [`2-library-organizer.toml`](config-examples/2-library-organizer.toml) | Add libraries for each category you use. Optionally add `[audiobookshelf]`. |
| Download my bookmarked books automatically | [`3-downloader.toml`](config-examples/3-downloader.toml) | Check `min_ratio` and the buffers. Start with `dry_run = true`. |
| Follow an author or look for new content | [`4-targeted-searches.toml`](config-examples/4-targeted-searches.toml) | Use `search_in`, `max_authors` and a sensible `cost`. |
| Get books from a Notion list | [`5-lists-and-audiobookshelf.toml`](config-examples/5-lists-and-audiobookshelf.toml) | Put the most specific `grab` block first. |
| See every available option | [`6-everything.toml`](config-examples/6-everything.toml) | Read it, do not copy it whole. |

Whichever you start from, work in this order: set `mam_id`, connect qBittorrent, get one library working
with a torrent you downloaded by hand, then add autograbs.

## Updating Fetchshelf

Before you update:

1. Read [RELEASES.md](../RELEASES.md). It lists what changed in each release and anything you must do when
   upgrading.
2. Back up your config and data folders (see [Backups](#backups-and-moving-to-a-new-machine)). Keep the
   backup until you are happy with the new version: it is the only way back.

With Docker:

```sh
docker pull ghcr.io/crimsonking60/fetchshelf:latest
docker stop fetchshelf && docker rm fetchshelf
# then run the same docker run command as before
```

With Compose:

```sh
docker compose pull fetchshelf
docker compose up -d fetchshelf
```

- Your configuration and database live in the mounted folders, so they survive the update.
- The database is upgraded automatically when the new version starts.
- **Going back to an older version is not supported.** The upgrade changes the database and nothing undoes
  those changes. An older version starts on the changed database without any warning, but can then fail.
  To go back, stop Fetchshelf, restore the backup you took before upgrading, and start the older version.
  Anything that happened after the upgrade is lost.
- To avoid surprise upgrades, use a version tag such as `ghcr.io/crimsonking60/fetchshelf:2026.10.2` instead of
  `latest`. To update, change the tag to the new version, then pull and recreate the container as above.
- After updating, open **Jobs** and check that the jobs run and finish without errors.

## Backups and moving to a new machine

What matters:

| What | Where | Why |
|---|---|---|
| `config.toml` | the `/config` folder | Your settings and secrets. |
| `data.db` | the `/data` folder | Everything Fetchshelf remembers: torrents, selections, events, errors. |

Your library and downloads are separate; back them up the way you normally do. Fetchshelf's own data is
small.

To back up, **stop the container**, copy both folders, then start it again. While Fetchshelf runs, its
latest changes are kept in a `data.db-wal` file next to `data.db`, so a copy taken while it runs can be
missing them or be unusable. When the container stops, they are written into `data.db` and the extra
files are removed. If `data.db-wal` or `data.db-shm` are still there after a stop, for example after a
crash, copy them as well and keep them next to `data.db`.

To move to another machine, install Fetchshelf there, stop it on both machines, copy `config.toml` and
everything in the data folder into the new config and data folders, and start it. Mount your media at the **same paths** as before (or adjust
`library_dir` and `path_mapping`), and use the same `PUID` / `PGID`, or fix the ownership of the copied
files.

## Security and privacy

- **There is no login.** Anyone who can open the web interface can start downloads, remove records and, with
  the torrent page actions, delete data from qBittorrent. Only expose Fetchshelf on a trusted network, or
  put it behind a reverse proxy that does authentication. Never publish port 3157 to the internet.
- **Secrets in `config.toml`:** `mam_id`, qBittorrent and Audiobookshelf credentials and a Notion token.
  Keep the file readable only by you, never post it unredacted, and never commit it to a public repository.
  The **Config** page hides secrets, but the file itself does not.
- **What leaves your machine.** Fetchshelf talks to MaM (searches, downloads, your account state),
  qBittorrent and Audiobookshelf (on your network), and, if you use it, Notion. It sends no usage data anywhere
  else.
- **Logs.** At every level, logs leave out your `mam_id`, passwords, API tokens, session cookies and the
  full web addresses Fetchshelf requests. They do contain each job's start, finish and error message, the
  names of your autograbs and lists, MaM torrent ids, and, when MaM refuses a request, the start of MaM's
  reply. At the default level, `debug`, they also contain the title of every search result an autograb
  rejects. Error messages can name hosts, folders and files on your system. Read a log before you share it
  and remove anything you consider private. Set `FETCHSHELF_LOG=information` to leave out the titles.

## Troubleshooting

First, look at the **Jobs**, **Errors** and **Events** pages and at `docker logs fetchshelf`.

### The container is unhealthy

Run `docker inspect --format '{{json .State.Health}}' fetchshelf` to see the last checks and their answers.
`Unhealthy` means Fetchshelf runs but cannot open its database: check the permissions on `/data` (see
below). *Connection refused* means Fetchshelf is not answering at all: read `docker logs fetchshelf`.

### The container exits or restarts

Run `docker logs fetchshelf`. If the message begins with *Invalid configuration*, fix the setting it names
(a misspelled key or name, a missing `url`, a library with both or neither of `category` and
`download_dir`, and so on) and restart.

### Permission errors on `/config` or `/data`

Set `PUID` and `PGID` to the owner of the host folders, or change the owner of the folders to match. If you
use `--user` or `user:` instead, make sure that user can write to both folders.

### Nothing runs

An empty `mam_id` means no jobs run. Also check the interval: `0` means the job only runs when you press
**Run now**.

### MaM login fails, or "too many requests", or the account is blocked

An authentication error means the session has expired or does not match your address. Create a new session
and update `mam_id`. For rate limiting, wait: the job tries again at its next interval. A blocked account (HTTP 406) aborts
downloads; wait and check your account on MaM.

### qBittorrent cannot be reached

Check the `url`, user name and password, and that the Web UI is enabled. In Docker, use the service or
container name rather than `localhost`, and make sure both containers share a network. Use
**Check qBittorrent** on the **Torrents** page to test.

### Linking fails or files do not appear

- *Source missing.* Fetchshelf cannot see the path qBittorrent reported. Add or fix `path_mapping`, or
  mount the same folder in both containers.
- *Cross-device link.* Hardlinks need the same filesystem. Put downloads and library under one mount, or use
  `hardlink_or_copy`.
- *No library matched.* The torrent has no category your libraries accept. Check your `[[tag]]` rules and
  `[[library]]` entries.
- *No author.* The torrent has no author in its metadata. Use **Edit** to add one.
- *No wanted file type.* The torrent has no extension from your `audio_types` / `ebook_types`.

### Nothing is being downloaded

The usual reasons, in order:

1. The autograb has `cost = "free"` (the default), and what it finds is not free.
2. The filter is too narrow, or the search returns nothing. Run the block with `dry_run = true` and read the
   log.
3. The budget is used up: no unsatisfied slots, not enough ratio budget, or the wedge reserve. The
   torrents stay on the **Selected** page.
4. The book is already in your library, or a duplicate of one, in an equal or better format. Skipped
   torrents are listed on the **Duplicates** page.
5. The torrent has none of your wanted file types.
6. The block's interval is `0`.

### A book went into the wrong folder

Open its torrent page and use **Edit** to fix its metadata, then **Refresh and relink**. Remember that the
author used is the **first** author.

### Audiobookshelf does not find a book

Make sure an Audiobookshelf library contains the folder `library_dir` points to, at the same path as in
Fetchshelf, and that Audiobookshelf has scanned it. The matcher runs every `interval` minutes, or press **Run now** on the **AbsMatcher** job.

### Getting more detail

Set `FETCHSHELF_LOG=debug` (the default) and, for a file you can attach to a report, set
`FETCHSHELF_LOG_DIR=/data/logs` and restart.

## Frequently asked questions

**Does it work without Audiobookshelf?** Yes. Audiobookshelf can still read the `metadata.json` files, or
you can use any other player.

**Can I use it without qBittorrent?** No. Both halves work through qBittorrent.

**Does it ever delete my downloaded data?** The cleaner only removes files that Fetchshelf itself linked,
and empty folders, `cover.jpg` and `metadata.json` in a book's own library folder. The one action that
deletes torrent data is **Remove from qBittorrent with data** on a torrent's page, which you click yourself.

**How do I stop it downloading?** Press the stop button on the container, or set the interval of the
autograbs to `0`. To pause only new downloads, set `add_torrents_stopped = true`.

**How do I download only audiobooks, or only ebooks?** Set `media_type = ["Audiobook"]` (or `["Ebook"]`) on
your autograbs, or `categories = { audio = true, ebook = false }`.

**How do I avoid anthologies?** Use `max_authors`, for example `max_authors = 3`.

**How do I stop a job running on a schedule?** Set its `search_interval` (or `link_interval`,
`import_interval`) to `0` and start it with **Run now**.

**Can I use more than one qBittorrent?** Yes, list several `[[qbittorrent]]` entries. New downloads go to
the first. Linking works against all of them.

**Can I prefer one format over another?** Yes, with the order of `audio_types` and `ebook_types`.

**Do config changes need a restart?** Yes.

## Getting help

- **Questions and bug reports:** `<support channel to be decided>`.
- **Include in a report:** the Fetchshelf version, what you expected and what happened, the relevant lines
  from the log, and your configuration with the secrets removed.
- **Remove secrets first:** replace `mam_id`, passwords and tokens with placeholders such as
  `replace-with-your-token`. Also check log lines for private paths and names.

## Open items for this guide

Tick each item when it is done. "You" means decisions or facts only the project owner can supply. "Me" means
work I can do on request.

### Decisions

- [x] **You:** Decide where the image is hosted (registry, name) and replace every `<registry>/<image>:<tag>`
  in [Installing with Docker](#installing-with-docker), [Installing with Docker Compose](#installing-with-docker-compose)
  and [Updating Fetchshelf](#updating-fetchshelf).
- [x] **You:** Decide the tagging scheme (`latest`, version numbers, release channels) and where release
  notes live, then update [Updating Fetchshelf](#updating-fetchshelf).
- [x] **You:** Decide the support channel (issue tracker, forum thread, chat) and fill in
  [Getting help](#getting-help). Decided: GitHub Discussions and issues; filling in the section is tracked in
  the [go-live plan](go-live-plan.md).
- [x] **You:** Decide whether the guide should say the project has no stable release yet, and where.
  Decided: no such note, the releases are stable.
- [x] **You:** Decide whether the image should get a `HEALTHCHECK`. If so, document it. The image has none
  today. Added: see [Health check](#health-check).
- [x] **You:** Review the suggested starting values in [Protecting your account](#protecting-your-account)
  (`min_ratio`, buffers) and confirm they are the advice you want to give. Confirmed.
- [x] **You:** Decide whether the repository `README.md` should link to this guide and the configuration
  reference.

### Facts to confirm

- [x] **You:** Check the MaM session steps in [Connecting to MaM](#connecting-to-mam) against the current MaM
  pages (menu names, the address/ASN lock options, dynamic IP wording). I wrote them from general
  knowledge, not from MaM itself. Kept as they are, with a pointer to MaM's documentation.
- [x] **You:** Confirm the statement on following MaM's rules, and add a link to the rules page if you
  want one. Confirmed; no link, since the rules are in a private area of MaM.
- [ ] **You:** Check the Notion steps in [Importing reading lists](#importing-reading-lists). They do not yet
  explain creating an integration, sharing the database with it, or finding the data source id. Tracked in
  the [go-live plan](go-live-plan.md).
- [x] **You:** Decide whether to replace or remove the Goodreads integration now that Goodreads has no RSS
  feeds, then rewrite or remove the Goodreads section in [Importing reading lists](#importing-reading-lists).
  Decided: removed.
- [ ] **You:** Check the Audiobookshelf steps in [Connecting to Audiobookshelf](#connecting-to-audiobookshelf),
  especially how to create the API token. Tracked in the [go-live plan](go-live-plan.md).
- [x] **Me:** Run the three-service Compose example against real qBittorrent and Audiobookshelf images and
  fix anything that does not work. Run with qBittorrent 5.3.0 and Audiobookshelf 2.37.1: linking and
  Audiobookshelf matching work. MaM itself was not part of the run.
- [x] **You:** Decide how the **Open in Audiobookshelf** link should work when `url` uses a Compose service
  name that browsers cannot resolve. Decided: recommend the host address in `url`.
- [x] **Me:** Read the Search page code and describe its filters accurately in
  [Finding and downloading books](#finding-and-downloading-books). I described it from the design, not the
  page.
- [x] **Me:** Audit what the logs contain at `debug` level and tighten or confirm the wording in
  [Security and privacy](#security-and-privacy).
- [x] **Me:** Confirm that downgrading after an upgrade is unsupported and that backups should be taken
  with the container stopped, then adjust [Updating Fetchshelf](#updating-fetchshelf) and
  [Backups](#backups-and-moving-to-a-new-machine) if needed.
- [x] **Me:** Check the pages table and the Torrents bulk actions against the running application, and
  add anything missing (for example the metadata problem finders, if and when they exist). The metadata
  problem finders do not exist yet; describe them here when they do.

### Content still to add

- [x] **You:** Provide screenshots for [First start](#first-start) and
  [Using the web interface](#using-the-web-interface), or tell me to leave them out. Decided: no screenshots.
- [x] **Me:** Add a short glossary (ratio, unsatisfied snatch, wedge, freeleech, VIP) if you want one for
  readers new to MaM. Decided against: MaM documents these terms, and the guide assumes readers know them.
- [ ] **Me:** Final read-through for consistency with the configuration reference, then check all links.
  Tracked in the [go-live plan](go-live-plan.md).
