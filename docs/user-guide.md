# Fetchshelf user guide

This guide is for people who run Fetchshelf. It assumes you use Docker, optionally with Docker Compose,
are comfortable editing a text file, and know how MAM works: ratio, unsatisfied snatches, freeleech, wedges
and VIP are not explained here. It does not cover building Fetchshelf from source.

> [!NOTE]
> This page follows the branch you are viewing: on `develop` it describes changes that are not released yet.
> For the version you run, see [this page on `main`](https://github.com/crimsonking60/fetchshelf/blob/main/docs/user-guide.md), which matches the latest release.

## Contents

- [What is Fetchshelf?](#what-is-fetchshelf)
- [Before you start](#before-you-start)
- [Installing with Docker](#installing-with-docker)
- [Installing with Docker Compose](#installing-with-docker-compose)
- [First start](#first-start)
- [Configuring Fetchshelf](#configuring-fetchshelf)
- [Connecting to MAM](#connecting-to-mam)
- [Connecting to qBittorrent](#connecting-to-qbittorrent)
- [Setting up your library](#setting-up-your-library)
- [Connecting to Audiobookshelf](#connecting-to-audiobookshelf)
- [Reading on an e-reader (OPDS)](#reading-on-an-e-reader-opds)
- [Finding and downloading books](#finding-and-downloading-books)
- [Automatic downloads](#automatic-downloads)
- [Snatchlists](#snatchlists)
- [Books you want, from Hardcover](#books-you-want-from-hardcover)
- [Completing series and following authors](#completing-series-and-following-authors)
- [Protecting your account](#protecting-your-account)
- [How finished downloads are organized](#how-finished-downloads-are-organized)
- [Using the web interface](#using-the-web-interface)
- [Background jobs](#background-jobs)
- [Getting notified](#getting-notified)
- [Common setups](#common-setups)
- [Updating Fetchshelf](#updating-fetchshelf)
- [Backups and moving to a new machine](#backups-and-moving-to-a-new-machine)
- [Security and privacy](#security-and-privacy)
- [Troubleshooting](#troubleshooting)
- [Frequently asked questions](#frequently-asked-questions)
- [Getting help](#getting-help)

## What is Fetchshelf?

Fetchshelf is a self-hosted service that connects MAM with the BitTorrent client qBittorrent. It downloads
and organizes anything MAM carries: ebooks, audiobooks, comics and manga, periodicals, musicology and radio.
It runs in the background, has a web interface for looking at what it is doing, and is configured with a
single text file.

It has two halves that you can use together or separately:

- **The auto-downloader** finds torrents on MAM that match rules you write (your bookmarks, a favourite
  author, new uploads in a category, and so on) and adds them to qBittorrent. While doing so it protects your
  account: it keeps your ratio above a limit, leaves some unsatisfied-snatch slots free and holds back a
  number of freeleech wedges. It can also look for the books on your *Want to Read* shelf or your lists on
  [Hardcover](https://hardcover.app).
- **The library organizer** watches qBittorrent for finished torrents and links the files into a tidy
  `Author/Series/Title` folder tree. Next to every title it writes a `metadata.json` file with the
  authors, series, description, ISBN, genres and more, taken from MAM (see
  [`metadata.json`](#metadatajson)), and, if you want, a [`metadata.opf`](#metadataopf) for Calibre. When a better format of a title arrives, it retires the older copy.

If you only want a tidy library, skip the auto-downloader. If you only want automatic downloads, skip the
organizer.

The library is plain folders and files, so you can point whatever you like at it: an ebook or comic
reader, an audiobook or music player, a file manager, a backup tool or your own scripts. Fetchshelf also
has an optional integration with [Audiobookshelf](https://www.audiobookshelf.org) (see
[Connecting to Audiobookshelf](#connecting-to-audiobookshelf)), but nothing else in Fetchshelf depends on
it.

This guide says **book** for any title on MAM, whatever its media type.

Fetchshelf is **not** a torrent client (qBittorrent does the downloading), not a reader or player (the apps
you point at your library do that) and has no user accounts of its own. See
[Security and privacy](#security-and-privacy).

## Before you start

You need:

- **A MAM account in good standing.** Fetchshelf acts on your behalf. You remain responsible for following
  MAM's rules, including ratio, seeding and unsatisfied-snatch requirements. Start with conservative
  settings (see [Protecting your account](#protecting-your-account)).
- **qBittorrent** with its Web UI enabled, reachable from the machine that runs Fetchshelf. MAM only allows
  certain client versions and changes the list over time, so check MAM's current list of approved clients
  before you install or upgrade qBittorrent.
- **Docker**, and optionally Docker Compose.
- **Audiobookshelf**, only if you want Fetchshelf to keep it up to date. Any other app that reads your
  library needs nothing from Fetchshelf.
- **Disk layout that allows hardlinks**, if you want files to appear in the library without using extra
  space. Hardlinks only work when the download folder and the library folder are on the **same
  filesystem** and, in Docker, reach Fetchshelf through the **same mount**. Otherwise Fetchshelf copies
  the files, which takes as much space again (see [Link methods](#link-methods)).

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

**NEVER run the `develop` image against a database or library you consider valuable. You risk losing both.**

Versions are written `YYYY.MM.N`: the year and month of the release, then a number that counts releases
within that month from `1`. Every release and what changed in it is listed in
[RELEASES.md](../RELEASES.md).

1. Create folders on the host for Fetchshelf's configuration and database:

   ```sh
   mkdir -p /srv/fetchshelf/config /srv/fetchshelf/database
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
     -v /srv/fetchshelf/database:/database \
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
| `/database` volume | Holds the database (`data.db`), where Fetchshelf remembers torrents, selections and events. |
| `/media` volume | Not required by Fetchshelf itself. A folder that holds both your downloads and your library, mounted as one, so linking can work. Give qBittorrent and, if you use it, Audiobookshelf the parts they need at the same paths (see [Together with qBittorrent and Audiobookshelf](#together-with-qbittorrent-and-audiobookshelf)). |
| `PUID` / `PGID` | The user and group id that Fetchshelf runs as inside the container. Set them to the owner of your config, data and media folders (run `id` on the host to see yours) so files are created with the right owner. Both default to `1000`. |

The container starts as root only to adjust ownership of `/config` and `/database`, then runs Fetchshelf as the
`PUID`/`PGID` user. If you start the container with a fixed user instead (`--user` or `user:`), `PUID` and
`PGID` are ignored and you must make sure that user can write to `/config` and `/database`.

Other environment variables you can set:

| Variable | Default | Meaning |
|---|---|---|
| `FETCHSHELF_CONFIG_FILE` | `/config/config.toml` | Where the configuration file is. |
| `FETCHSHELF_DATA_FILE` | `/database/data.db` | Where the database is. |
| `FETCHSHELF_LOG` | `information` | Log level: `verbose`, `debug`, `information`, `warning` or `error`. |
| `FETCHSHELF_LOG_DIR` | empty | If set, also write daily rotating log files to this folder. Logs always go to the container output. |
| `FETCHSHELF_CONF_<setting>` | | Override a configuration setting. See [Configuring Fetchshelf](#configuring-fetchshelf). |

### Health check

The image has a built-in health check. Every 30 seconds Docker asks Fetchshelf whether it is running and can
open its database, and `docker ps` shows the container as `healthy` or `unhealthy`. The check follows
`web_port`, so it keeps working if you change the port. It does not check MAM, qBittorrent or
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
      - /srv/fetchshelf/config:/config
      - /srv/fetchshelf/database:/database
      - /srv/media:/media
```

Common commands, run in the folder that contains the file:

| Command | What it does |
|---|---|
| `docker compose up -d` | Create and start the container. |
| `docker compose logs -f fetchshelf` | Follow the log. |
| `docker compose exec fetchshelf dotnet /app/Fetchshelf.Web.dll --check-config` | Check `config.toml` without restarting: prints what Fetchshelf would load, or why it would not start. |
| `docker compose restart fetchshelf` | Restart, for example after editing `config.toml`. |
| `docker compose stop` / `docker compose down` | Stop, or stop and remove the container. Your config and data stay in the mounted folders. |

### Together with qBittorrent and Audiobookshelf

If you run all three in one Compose project, keep one media folder on the host, `/srv/media`, with the
downloads in `/srv/media/torrents` and the library in `/srv/media/library`. Each container mounts only what
it needs, but always at the same path inside the container:

| Container | Needs | Mount |
|---|---|---|
| qBittorrent | The downloads. | `/srv/media/torrents:/media/torrents` |
| Audiobookshelf | The library. | `/srv/media/library:/media/library` |
| Fetchshelf | Both, through **one** mount. | `/srv/media:/media` |

Because the paths are the same everywhere, the path that qBittorrent reports for a download is also valid
inside Fetchshelf, so no `path_mapping` is needed, and Audiobookshelf sees each book at the same path as
Fetchshelf, which it needs to match them.

Fetchshelf must mount the shared parent folder, not `torrents` and `library` separately. A hardlink cannot
cross from one mount to another, even when both are on the same disk, so with two separate mounts every
hardlink fails and Fetchshelf copies the files instead, using twice the space.

If you do not use Audiobookshelf, leave out its service. Any other app that reads your library just needs
access to `/srv/media/library`.

Create the folders before the first start, so they belong to your user rather than to root:

```sh
mkdir -p /srv/media/torrents /srv/media/library
```

```yaml
services:
  qbittorrent:
    # Use a version tag from MAM's list of approved clients instead of latest.
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
      - /srv/qbittorrent/config:/config
      - /srv/media/torrents:/media/torrents

  audiobookshelf:
    image: ghcr.io/advplyr/audiobookshelf:latest
    container_name: audiobookshelf
    restart: unless-stopped
    ports:
      - "13378:80"
    volumes:
      - /srv/audiobookshelf/config:/config
      - /srv/audiobookshelf/metadata:/metadata
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
      - /srv/fetchshelf/config:/config
      - /srv/fetchshelf/database:/database
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
   cannot see them. If you turn on **Keep incomplete torrents in**, or give categories their own save
   paths, keep those under `/media/torrents` too: it is the only media folder qBittorrent can write to.

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
4. Edit `config.toml` in the config folder you mounted, check it with `--check-config` (see
   [Configuring Fetchshelf](#configuring-fetchshelf)), then restart the container.

When it is working, the **Jobs** page lists your jobs with their last run time and result, and the
container log shows each job starting and finishing.

## Configuring Fetchshelf

All settings live in one TOML file, `config.toml`, in the folder mounted at `/config`.

- **Where to start.** Pick the closest of the [numbered examples](config-examples/README.md), copy it to
  `config.toml` and replace the `replace-with-...` placeholders. Example 1 is the smallest, example 5
  shows every option.
- **Reference.** Every setting is described in the [configuration reference](configuration.md).
- **Mistakes are caught at startup.** Fetchshelf refuses to start with a setting it does not know, a missing
  required value, a number out of range (such as `min_ratio = 0` or a negative interval), or an unrecognized
  name or value in any filter (for example a misspelled language, category or size). The error message names
  the setting and the section it is in. Check `docker logs fetchshelf`.
- **Check before restarting.** After editing `config.toml`, run
  `docker exec fetchshelf dotnet /app/Fetchshelf.Web.dll --check-config`. It loads the file the way a restart
  would, and prints either the libraries and jobs it found or the error that would stop Fetchshelf, without
  touching the running instance. See [Checking a configuration](configuration.md#checking-a-configuration).
- **Changes need a restart.** Fetchshelf reads the file when it starts. After editing it, restart the
  container. Restarting is safe at any time: queued selections, known torrents, events and errors are kept
  in the database and picked up again after the restart. A job that the restart cuts off does its work on
  its next run, and a download that already reached qBittorrent is recognised there rather than added
  twice. The **Jobs** page keeps each job's last run and history, and a cut-off run shows as *Interrupted*.
  See [when a job first runs after a restart](#when-a-job-first-runs-after-a-restart).
- **Environment overrides.** Any simple top-level setting can be set through an environment variable named
  `FETCHSHELF_CONF_<setting>` with the setting name in lower case, for example
  `FETCHSHELF_CONF_mam_id`. Environment variables win over the file. This is one way to keep a secret such
  as `mam_id` out of a file you share; `mam_id_file` is another (see
  [Keeping the session id in a separate file](#keeping-the-session-id-in-a-separate-file)). Sections such
  as `[[qbittorrent]]` cannot be overridden this way.
- **Intervals** are in minutes. `0` means "never on a schedule, only when I press **Run now**".

## Connecting to MAM

Fetchshelf logs in to MAM with a session id, the value of a session cookie that you create yourself.

1. Sign in to MAM and open your security preferences.
2. Create a new session. MAM ties a session to an address, so choose how it is locked to match where
   Fetchshelf runs. If your home IP address changes, pick the option that tolerates that.
3. Copy the session id and put it in `config.toml`:

   ```toml
   mam_id = "your-session-id"
   ```

4. Optionally, check that MAM accepts the session id. On the machine that runs Fetchshelf, run:

   ```sh
   curl -b "mam_id=your-session-id" https://www.myanonamouse.net/json/checkCookie.php
   ```

   This is the same check Fetchshelf makes. If the answer contains `"Success":true`, the session id works;
   anything else means MAM did not accept it. Because a session is locked to an address, run the command
   where Fetchshelf's traffic comes from: if Fetchshelf goes through a VPN, so must the command.

5. Restart the container. The jobs now start.

For more about sessions and how MAM locks them, see MAM's own documentation.

### Keeping the session id in a separate file

Instead of `mam_id`, you can set `mam_id_file` to the full path of a file that holds only the session id.
That keeps it out of `config.toml`, so you can share the config file without it. Set one or the other:
setting both stops Fetchshelf from starting. A trailing newline in the file is fine.

With Docker Compose, a secret is a convenient way to provide the file. Save the session id in
`/srv/fetchshelf/secrets/mam_id` on the host, make it readable by the `PUID` user, and add:

```yaml
services:
  fetchshelf:
    # ...as before...
    secrets:
      - mam_id

secrets:
  mam_id:
    file: /srv/fetchshelf/secrets/mam_id
```

Compose makes the secret available inside the container as `/run/secrets/mam_id`, so `config.toml` has:

```toml
mam_id_file = "/run/secrets/mam_id"
```

With `docker run`, mount the file yourself: `-v /srv/fetchshelf/secrets/mam_id:/run/secrets/mam_id:ro`.
Fetchshelf reads the file when it starts, so after changing it, restart the container. To check the session
id in the file, without typing it into your shell history:

```sh
curl -b "mam_id=$(cat /srv/fetchshelf/secrets/mam_id)" https://www.myanonamouse.net/json/checkCookie.php
```

Things to know:

- **Treat `mam_id` like a password.** Anyone who has it can act as you on MAM.
- **Fetchshelf always uses the `mam_id` from your configuration.** It does not store or switch to a newer
  session id, so the one you set is the one it sends until you change it and restart.
- **Rate limits.** If MAM answers "too many requests", the job fails with an error and tries again at its
  next interval.
- **Blocked account.** If MAM temporarily blocks the account, any running download aborts. Wait, then
  check the account on MAM before running jobs again.
- **Expired or invalid session.** When MAM refuses a request (HTTP 403), usually because the session has
  expired or is locked to a different address, Fetchshelf pauses all its MAM requests for 30 minutes, and
  the job's error says until when. Check the session id with the `curl` command above. If MAM does not
  accept it, create a new session, update `mam_id` (or the file `mam_id_file` points to) and restart:
  restarting also ends the pause.

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

New downloads go to the **first** `[[qbittorrent]]` entry, but linking checks every entry. If you also
run another qBittorrent, for example on a second machine, add it as another entry: the torrents you add
there yourself are then linked into your library too. Fetchshelf must be able to reach that client's
download folder, directly or through that entry's own `path_mapping`.

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

### `on_unlinked`

The same for a book you take out of the library yourself with **Unlink** (see
[Unlinking a book you don't want](#unlinking-a-book-you-dont-want)), for example to move it to a category
no library uses:

```toml
[qbittorrent.on_unlinked]
category = "unlinked"
```

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
added. An `[[autograb]]` or a `[[hardcover_list]]` can also force a category with its own `category`
setting.

Libraries are not limited to audiobooks and ebooks: add one for anything you want kept apart. For
example, to give comics and manga their own folder, put this rule **before** the ebooks rule, so it
claims them first:

```toml
[[tag]]
media_type = ["ComicBook", "Manga"]
category = "comics"

[[library]]
name = "Comics"
category = "comics"
library_dir = "/media/library/comics"
```

Rules leave out Musicology and Radio unless you include them, so a rule for those needs both the media
type and the category switch, for example `media_type = ["Radio"]` with `categories = { radio = true }`
(see the [shared torrent filter](configuration.md#the-shared-torrent-filter)).

You can narrow a library with `allow_tags` (the torrent needs at least one) and `deny_tags` (the torrent
is rejected if it has any), and give it its own `audio_types` / `ebook_types` to control which files are
linked into it.

### Link methods

`method` controls how a file gets from the download folder to the library:

| Method | Behaviour | When to use it |
|---|---|---|
| `hardlink` | A second name for the same file. No extra space. Needs the same filesystem; fails otherwise. | Downloads and library share a filesystem, and you would rather see an error than use space on a copy. |
| `hardlink_or_copy` (default) | Hardlink, falling back to a copy. | Most setups: no extra space where hardlinks work, a copy where they don't. |
| `hardlink_or_symlink` | Hardlink, falling back to a symbolic link. | As above, but you prefer links to copies. |
| `copy` | A full copy. | The library is on a different disk. Uses extra space. |
| `symlink` | A symbolic link to the download. | Rarely. Some programs cannot follow links. |
| `no_link` | Records metadata only; no files are placed. | You only want tracking. |

Seeding keeps working with hardlinks and symlinks, because the original files stay where qBittorrent put
them.

To use the same method for all your libraries, set it once at the top of the file instead of in each
`[[library]]`. A library's own `method` still wins:

```toml
link_method = "hardlink"
```

## Connecting to Audiobookshelf

Skip this section if you do not use Audiobookshelf. It is the one app Fetchshelf talks to directly, but
the library and its `metadata.json` files are the same with or without it.

Even without this section, Audiobookshelf reads the `metadata.json` file that Fetchshelf writes next to
every book. With it, Fetchshelf also:

- finds each linked book in Audiobookshelf and stores the link, shown on the torrent's page;
- pushes updated metadata to Audiobookshelf after linking or a refresh;
- deletes the old Audiobookshelf item when a book is replaced by a better format in another folder, or
  when you **Unlink** it.

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

3. Make Audiobookshelf see your library at the **same path** as Fetchshelf, or tell Fetchshelf how the paths
   differ. Fetchshelf finds a book in Audiobookshelf only if one of the Audiobookshelf library's folders
   contains the path where Fetchshelf linked the book. If Fetchshelf's `library_dir` is
   `/media/library/audiobooks`, the simplest is to mount the same folder at `/media/library/audiobooks` in
   the Audiobookshelf container and add that folder to the Audiobookshelf library. To give Audiobookshelf
   only the library, at a path of its own such as `/audiobooks`, add a `path_mapping` from Audiobookshelf's
   path to Fetchshelf's, the same way round as qBittorrent's (see
   [`[audiobookshelf]`](configuration.md#audiobookshelf)):

   ```toml
   [audiobookshelf.path_mapping]
   "/audiobooks" = "/media/library/audiobooks"   # Audiobookshelf's path = Fetchshelf's path
   ```

   If the paths differ and no mapping translates them, no book is ever matched.

`interval` is how many minutes pass between matching runs. Remove the whole section to switch the
integration off.

## Reading on an e-reader (OPDS)

Fetchshelf can serve your ebook libraries as an OPDS catalog, the format e-readers and reading apps use to
browse a library and download books: KOReader, Moon+ Reader, Thorium, Panels, KyBook and many others.

1. **Turn it on** by adding an `[opds]` section to `config.toml`, empty to serve the ebooks in every
   library, or with the libraries to serve (see [`[opds]`](configuration.md#opds)):

   ```toml
   [opds]
   libraries = ["Ebooks"]
   ```

2. **Restart Fetchshelf.** The **Config** page shows the catalog address, for example
   `http://192.168.1.20:3157/opds`.
3. **Add it to your reader.** In KOReader: **Search → OPDS catalog → +**, with the catalog address as the
   URL. In other apps, look for "Add catalog", "OPDS" or "Library server". Leave the username and password
   empty, unless you put a reverse proxy with a login in front (see below).

The catalog offers:

- **Recently added,** **Authors** (each author's books by series and number) and **Series** (in series
  order), and **Libraries** when more than one is served. Each library has its own **Recently added**,
  **Authors** and **Series**, for browsing just your comics, say. Long lists come 50 to a page.
- **Search** by title, author or series, the same way as on the **Torrents** page. It always searches every
  served library.
- **Each book** with its title, authors, language and genres from MAM. Its series and number are shown at
  the start of its description, since OPDS has no series field.
- **Downloads** of the ebook file Fetchshelf linked for the book, named like `Title - Author.epub`. A book
  split into several files offers each part.
- **Covers** read from EPUB and CBZ files. PDFs and other formats have no cover in the catalog. Covers are
  kept in a `covers` folder next to the database, so each book is only opened once.

What it serves: every linked book in a served library whose MAM media type is Ebook, Manga, Comic Book or
Periodical Ebook. Audiobooks are never served, nor is the PDF that comes with some audiobooks. A newly linked
book appears within a minute.

The catalog has no login, like the rest of Fetchshelf: anyone who can reach it can browse and download your
books. Use it on your home network, or, to read away from home, put it behind a reverse proxy that asks for
a username and password. OPDS readers can send those. See [Security and privacy](#security-and-privacy).

## Finding and downloading books

The **Search** page is also the home page. Use it to search MAM yourself and download a book by hand.

- **Search** looks for your text in the title and author of every torrent on MAM. With the box empty, the
  page shows the newest torrents on MAM instead.
- **Search in**, below the box, opens a row of checkboxes: title, author, series, narrator, description,
  tags and file names. Tick the ones to look in; with none ticked, title and author are used.
- **The type list** limits what is searched: *All torrents*, *Freeleech*, *Freeleech or VIP*, *VIP*,
  *Bookmarked on MAM* (the account's bookmarks), or *Not snatched yet*. MAM can't be asked for torrents the
  account hasn't snatched, so that last one searches everything and leaves out, on each page, the ones it has;
  the line below the results says how many. A result the account has snatched before is marked **Snatched**
  whatever the type.
- **The sort list** sets the order: *Best match*, *Newest*, *Oldest*, *Most seeders*, *Most snatched*,
  *Largest* or *Title*. *Best match* is MAM's own order: by how well a result matches the search text; with
  the box empty, newest first, and for *Bookmarked on MAM* the newest bookmarks first.
- **Pages.** A page holds 100 results. The line below them says which results these are out of how many MAM
  found, with **Previous** and **Next** to move between pages. Changing the search, the type, the sort or
  where to search in starts again at the first page. The address holds all of it, so a search can be
  bookmarked or shared.
- **The language list** next to the box limits the search to a language. It starts at *Your languages*, the
  top-level `languages` in your configuration (see [Filtering](#filtering)); choose *Any language* or one
  language for a search of its own. Without a top-level `languages`, it starts at *Any language*. Unlike the
  filters below, the language is part of what is asked of MAM, so it does change which results come back.
- **Hide downloaded torrents** hides every result that has a status or is in qBittorrent (see below), so
  only torrents you don't have yet remain.
- **Show** limits the list to one kind of torrent:

  | Choice | Shows |
  |---|---|
  | All | Every result. |
  | Ebooks | Ebooks and periodical ebooks. |
  | Audiobooks | Audiobooks and periodical audiobooks. |
  | Comics | Manga, and comic books and graphic novels. |
  | Other | Everything else, such as musicology and radio. |

  These two filters only hide rows in the results already on the page, and the line below the results says
  how many they hide. They do not change what is asked of MAM, so they cannot bring in more results, and they
  are reset by a new search or a change of page.
- Click a result's title for everything MAM returns for it: cover, narrators, series, language,
  categories, flags, tags, ISBN or ASIN, files, uploader, seeders and leechers, the description, the media
  info (duration, format, bitrate and more, where MAM has it) and every raw field, plus whether Fetchshelf
  and qBittorrent have it. The details open over your results, which stay as they were, filters and all:
  **Close**, Escape or a click beside the details takes you back. Open the title in a new tab for a page of
  its own.
- Each result shows its title, authors, media type, metadata, size and status. The status says where the torrent is
  in Fetchshelf: **Selected** (waiting to be downloaded), **Downloading** (added to qBittorrent and not
  finished yet), **Downloaded** (finished, or owned through a snatchlist, but not in your library) or
  **Linked** (in your library). It is empty for torrents Fetchshelf has not handled. Until a torrent is
  **Linked**, its status checks again by itself, every 10 seconds for the first minute and then every
  minute, so it moves on without a reload. It stops checking after about an hour. While a torrent is
  **Downloading**, each check asks qBittorrent about it; once qBittorrent has finished it, the Link job
  starts straight away rather than at its next interval, so **Linked** follows within seconds. A finished
  torrent shows **Downloaded** when no library accepts it.
- **Metadata** rates how complete the torrent's information on MAM is, which decides how well Fetchshelf can
  name, file and describe the book. Point at the pill to see what is missing.

  | Pill | Means |
  |---|---|
  | **Good** (green) | Author, file types, description, cover, genres, a narrator for audiobooks, and an ISBN or ASIN, or all but one small item. |
  | **Lacking** (yellow) | No real description, or two small items missing. |
  | **Poor** (red) | No author (such a torrent can't be linked into a library), no file types (Fetchshelf can't tell its format, so it never downloads it), or more missing than for *Lacking*. |

  A description with fewer than 80 characters of text counts as missing. When several uploads of the same
  book are on offer, the pill helps pick the one whose library folder and `metadata.json` will come out best.
- Next to it, **qBittorrent: *category*** marks a torrent qBittorrent has, with its category there, also when
  you added it outside Fetchshelf. Fetchshelf recognises those by the MAM id MAM writes into every
  `.torrent` file; this needs qBittorrent 5. **Not in qBittorrent** marks a torrent Fetchshelf downloaded
  or linked that qBittorrent no longer has. Fetchshelf reads qBittorrent's torrent list at most every two
  minutes, in the background, so a torrent added a moment ago can take that long to show.
- The **Cost** column marks **FL** (freeleech), **Personal FL** (personal freeleech) and **VIP** torrents.
- **Download into**, above the results, chooses the qBittorrent category a download gets, and with it the
  library it ends up in. It offers each library's category, then every other category qBittorrent has
  torrents in. Left at *the category the tag rules give*, the `[[tag]]` rules decide, as for automatic
  downloads. Tags always come from the `[[tag]]` rules. The browser remembers the choice, and the message
  after a download names the category used. A remembered category that is no longer offered, for example
  while qBittorrent doesn't answer, falls back to the tag rules.
- **Download** adds the torrent as it is. **Wedge download** spends a freeleech wedge so the torrent
  costs no ratio. A message at the top of the page says whether the torrent was selected, and if not, why:
  for example, that a copy is already in your library. A selected torrent's status then follows it into the
  library by itself (see below). With `download_after_selection = false` the buttons say **Select** and
  **Wedge select**, since the torrent then waits on the **Selected** page for the next Download run.
- **Can't download** replaces the download buttons on a torrent Fetchshelf would refuse anyway: one whose
  MAM listing has no file types, or whose file types are all missing from your format list for its kind of
  book (`ebook_types`, `audio_types`, `music_types` or `radio_types`). Point at it to see which. In the
  second case, adding one of its file types to that list makes it downloadable.
- **Import** replaces the two download buttons on a torrent qBittorrent already has but Fetchshelf doesn't
  know, such as one you added by hand. It records the torrent with MAM's details without downloading
  anything, so it shows on the **Torrents** page. Its category in qBittorrent stays as it is: if a library
  uses that category, the next Link run links it; otherwise give it one with **Set category** on the
  **Torrents** page. A torrent that Fetchshelf already has and qBittorrent too gets no buttons.
- If you set `wedge_over` in the `[search]` section, results larger than that size have the
  **Wedge download** button highlighted:

  ```toml
  [search]
  wedge_over = "100 MiB"
  ```

A manual download goes through the same steps as an automatic one. It becomes a **selection**, which the
downloader processes with the ratio, unsatisfied-slot and wedge checks (see
[Protecting your account](#protecting-your-account)) and then adds to qBittorrent with the category and tags
from the `[[tag]]` rules, or the category chosen under **Download into**. The download starts straight away,
unless `download_after_selection = false`
makes selections wait for the next scheduled Download run. You can follow it on the **Selected** page.

If qBittorrent already has the torrent, in any category, Fetchshelf doesn't add it again and spends no
wedge on it. It records the torrent, which then shows on the **Torrents** page, and leaves its category in
qBittorrent as it is. The Link job links it once its category or save path matches one of your libraries.

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
| `"bookmarks"` | Your MAM bookmarks. |
| `"freeleech"` | MAM's global freeleech list. |
| `"new"` (or `"torrents"`) | All torrents on MAM, newest first. |
| `"mine"` | Your own uploads. |
| `{ uploader = 12345 }` | One uploader's torrents. |

`type` is required. The name `new` is misleading: it searches **all** torrents on MAM, and "new" only
describes the default ordering, newest first.

### How much a run reads: `max_pages`

MAM returns search results in pages of 100. `max_pages` is the most pages one run reads:

| `type` | Default `max_pages` | So a run reads at most |
|---|---|---|
| `"bookmarks"`, `"freeleech"`, `"mine"` | 50 | 5,000 results, in practice the whole list |
| `"new"`, `{ uploader = ... }` | 1 | the first 100 results |

A run reads page 1, then page 2 and so on. It stops early when a page comes back with fewer than 100
results, because there are no more, or when it has selected as many torrents as it may (see
[Protecting your account](#protecting-your-account)). Each page is one search request to MAM.

- **Results are not yet matches.** Fetchshelf asks MAM to narrow the search with your `query` and as much of
  the filter as MAM supports, then checks the full filter itself. A page of 100 results can therefore hold
  fewer than 100 torrents that pass your filter.
- **Every run starts again from page 1.** Fetchshelf does not remember where the last run stopped.
  Torrents it has already selected or has in your library are recognised and skipped, but they still take
  up room on the page.
- **One page keeps up with new uploads.** With the default order and one page, each run sees the 100
  newest results. That is enough for a block that runs every few minutes or hours, as long as fewer than
  100 new results can appear between two runs. It never looks further back.
- **More pages look further back,** for example for a backfill. Each extra page is one more request to MAM
  on every run, so use as few as do the job. A block with many pages that you only need once is best
  run by hand with `search_interval = 0` (see [Trying a block out safely](#trying-a-block-out-safely)).

`[[snatchlist]]` blocks have a `max_pages` of their own, with a default of 50. MAM decides how many rows a
snatch list page holds, and has been seen sending 1000, so one page can already be your whole list.

### In what order: `sort_by`

`sort_by` decides the order of the results, and with it which ones a run reaches within its `max_pages`:

| `sort_by` | Order |
|---|---|
| `"newest_first"` (default) | Newest uploads first; for `bookmarks`, the newest bookmarks first. |
| `"oldest_first"` | Oldest uploads first. Together with a higher `max_pages`, good for a one-off backfill. |
| `"random"` | A different random selection on every run. |
| `"low_seeders"` | Fewest seeders first: torrents that need seeding most. |
| `"low_snatches"` | Fewest completed downloads first. |
| `"high_seeders"` | Most seeders first. |
| `"high_snatches"` | Most completed downloads first: the most popular. |
| `"largest_first"` | Largest torrents first. |
| `"title"` | By title, A to Z. |

### Filtering

Every autograb can use the same filter fields: media type, categories, languages, flags such as `explicit`
or `abridged`, file size, uploader, upload date, seeders, leechers, times snatched, and `max_authors` (which
skips anthologies, keeping a book like Good Omens with two authors). A torrent must satisfy **every** field
you set. The details are in the [configuration reference](configuration.md#the-shared-torrent-filter).

If most of what you want is in the same languages, set them once at the top of the file instead of in every
block:

```toml
languages = ["English"]          # the default for every autograb and Hardcover list grab block

[[autograb]]
name = "New ebooks"              # no languages here: English, from the top
type = "new"
cost = "free"

[[autograb]]
name = "Freeleech in any language"
type = "freeleech"
cost = "free"
languages = []                   # any language, whatever the top-level setting says
```

A block's own `languages` always wins, and `languages = []` means any language. The Search page starts at
these languages too. **`[[tag]]` rules are the exception: they never use the top-level `languages`.** A tag
rule without its own `languages` matches every language, so a book selected in another language still gets
its category and reaches its library.

### What to pay: `cost`

| `cost` | Meaning |
|---|---|
| `free` (default) | Only freeleech or VIP torrents. |
| `ratio` or `all` | Pay with ratio. Free torrents still cost nothing. |
| `wedge` | Always use a wedge on torrents that are not already free. With no wedge to spare above `wedge_buffer`, the torrent waits on the **Selected** page. |
| `try_wedge` | Use a wedge if one is available above your reserve, otherwise pay with ratio. |
| `metadata_only` | Never download; just refresh the details of books Fetchshelf already has. |
| `metadata_only_add` | Like `metadata_only`, but also records the books Fetchshelf does not have yet, still without downloading them. Fetchshelf then treats those books as already in your library: it will not download them again unless it finds a better format. |

VIP, freeleech and personal-freeleech torrents are always downloaded for free, whatever `cost` says:
`ratio` spends no ratio on them and `wedge` no wedge. So a `free` block is only needed when you want nothing
*but* free torrents.

### Using several blocks together

Blocks are independent, so a common pattern is one block per cost level:

```toml
[[autograb]]
name = "Free bookmarks"         # take everything that is free
type = "bookmarks"
cost = "free"

[[autograb]]
name = "Small bookmarks"        # small files cost little ratio
type = "bookmarks"
cost = "all"
max_size = "30 MiB"

[[autograb]]
name = "Other bookmarks"        # use wedges for the rest
type = "bookmarks"
cost = "try_wedge"
```

Give each block a `name`. Each block is its own job, and the **Jobs** page lists it, with its **Run now**
button, under that name, so you can tell "Free bookmarks" from "Small bookmarks" at a glance. Without
names the jobs are only called `autograb[0]`, `autograb[1]` and so on, by their position in the file. The
name also shows on the **Selected** page and in **Events** as what chose each torrent, and in the log
lines of each run. Without a name the log only gives the block's `type`, which is the same for all three
blocks above.

### Trying a block out safely

- `dry_run = true` runs the whole search and selection but selects, records and downloads nothing. It only
  logs what it would do. To try out a new block:

  1. Add `dry_run = true` to the block and restart Fetchshelf.
  2. On the **Jobs** page, press **Run now** for the block, so you do not have to wait for its interval.
  3. Read the log (`docker logs fetchshelf`). There is a line for each search result that passes the
     filter, saying whether it would be selected or why it would be skipped (for example a duplicate, or
     not free), and a last line with how many would have been selected. Results the filter rejects are
     only listed at the `debug` log level.
  4. If the list is not what you want, change the block's settings, restart and run it again.
  5. When it is, remove `dry_run`, restart, and press **Run now** to start the first real run, or wait
     for the block's interval.

  "Would be selected" means the torrent would be queued. Your ratio, unsatisfied-snatch slots and wedges
  are only checked when a download starts, so a queued torrent can still wait on the **Selected** page.
- `search_interval = 0` stops the block running by itself. Start it from the **Jobs** page with **Run now**.
  This suits a one-off backfill: a block that runs only when you press **Run now**, usually with
  `sort_by = "oldest_first"` and a higher `max_pages`. Check it with `dry_run = true` first, as above, then
  remove `dry_run`, restart and press **Run now** once.
- `max_active_downloads` (which needs a `name`) caps how many unfinished downloads one block may have.

Examples of each pattern are in [`4-targeted-searches.toml`](config-examples/4-targeted-searches.toml).

## Snatchlists

A `[[snatchlist]]` reads one of your MAM snatch lists without downloading anything. With
`cost = "metadata_only_add"` it records every torrent on the list as something you already own, which
stops Fetchshelf downloading a book you got some other way. With the default, `metadata_only`, it only
refreshes the details of torrents Fetchshelf already has. See
[`4-targeted-searches.toml`](config-examples/4-targeted-searches.toml).

The first `metadata_only_add` run on a long list looks every torrent up on MAM once, so it can take a while.
Later runs read the list and look up only what is new. Details you edited by hand on a torrent's page are
kept; a snatchlist only updates their VIP status.

## Books you want, from Hardcover

[Hardcover](https://hardcover.app) is a site for keeping track of what you read and want to read. A
`[[hardcover_list]]` follows one reading status there, usually *Want to Read*, or one of your Hardcover
lists, and searches MAM for each book on it. What it finds goes through the same rules as an autograb's,
so your ratio, unsatisfied-snatch slots and wedges are protected the same way. Fetchshelf only reads from
Hardcover; it never changes anything there.

1. **Create a token.** On Hardcover, open **Settings → Hardcover API** and choose **New API Key**. Give it
   the permissions `read:me`, `read:library` and, if you will follow a list, `read:lists`, and an expiry
   date you will remember: when it passes, the token stops working.
2. **Add it to `config.toml`**, or to a file named by `token_file`:

   ```toml
   [hardcover]
   token = "your-hardcover-api-token"
   ```

3. **Add a `[[hardcover_list]]`** with one or more `grab` blocks, which work like an autograb's `cost` and
   filter:

   ```toml
   [[hardcover_list]]
   name = "Want to Read"
   status = "want_to_read"
   prefer_format = "audio"     # leave out to get both the audiobook and the ebook

   [[hardcover_list.grab]]
   cost = "free"
   languages = ["English"]
   ```

   For one of your lists, write `list = "books-to-fetch"` instead of `status`, with the last part of the
   list's address.

   To send a list's downloads to a library of their own, give it a `category`. For example, with fiction
   and nonfiction kept in separate libraries:

   ```toml
   [[hardcover_list]]
   name = "Fiction"
   list = "fiction"
   prefer_format = "ebook"
   category = "MAM_fiction"     # a [[library]] with category = "MAM_fiction" picks these up

   [[hardcover_list.grab]]
   cost = "free"

   [[hardcover_list]]
   name = "Nonfiction"
   list = "nonfiction"
   prefer_format = "ebook"
   category = "MAM_nonfiction"

   [[hardcover_list.grab]]
   cost = "free"
   ```

4. **Restart Fetchshelf.** The list gets a job on the **Jobs** page, and **Lists** appears in the menu.
   Press **Run now** to try it straight away.

On each run Fetchshelf reads the list from Hardcover and, for every book still missing a format, first
checks your library (including books a snatchlist recorded), then searches MAM by title and author with
each `grab` block in turn. Each block only takes a torrent its `cost` accepts, so blocks like `free`, then
`wedge`, then `ratio` try the cheapest way first. One block with the most you are willing to pay works as
well: VIP and freeleech torrents are always downloaded for free, whatever the cost says. A result must match
the title and an author closely; Hardcover and MAM sometimes
name a book differently, so the **Lists** page has a **Search MAM** link on every book for the ones it
misses. A book that is still missing a format is searched for again once a day, not on every run, and
books you take off the list on Hardcover are no longer searched for.

Use `dry_run = true` first to see in the log what a list would select. See
[`[[hardcover_list]]`](configuration.md#hardcover_list) for every setting.

Hardcover's API is still in beta, so Hardcover may change it or reset tokens without notice. If the job
starts failing, see [A Hardcover list fails](#a-hardcover-list-fails).

## Completing series and following authors

The **Series** page lists every series in the library: its authors, the books it has (*1–4, 6*), the books
missing between them (*5*), and the media types. Books with a number like 1.5, usually novellas between two
books, are shown when owned but never counted as missing. It can be limited to series with missing books,
followed series or ignored ones, and searched by series or author name. Each series name leads to its books
on the **Torrents** page.

With a [`[follow]`](configuration.md#follow) section, series and authors can be followed:

```toml
[follow]
category = "audiobooks"        # optional: the library the books go to

[[follow.grab]]
cost = "free"
categories = { ebook = false }
```

- **Follow** a series on the **Series** page: the Follow job then downloads its missing books, and new ones
  as they are uploaded, through the same rules as automatic downloads.
- **Watch** a series instead to have what is found listed on the **Following** page, with **Download**,
  **Wedge download** and **Dismiss** buttons, and nothing downloaded by itself.
- **Ignore** a series to leave it out of the series with missing books.
- **Follow an author** with the button on a torrent's page, or next to an author filter on the **Torrents**
  page. An author is watched to begin with: their new books are listed, not downloaded. The **Following**
  page switches a follow between downloading automatically and only listing.

The **Following** page lists every followed or watched series and author with what was found for it; the
count on its menu link is what is waiting to be looked at. A find marked *not free* matched, but none of the
`[[follow.grab]]` blocks pays for it; **Download** or **Wedge download** still takes it. **Search now** puts a
series or author first in line and starts the Follow job.

Each run makes at most `searches_per_run` MAM searches (10 by default), and searches each series or author
at most once every `search_again_after` hours (24). Fifty followed series are then each searched once a day,
in five runs. This keeps the number of requests to MAM low. Use
`dry_run = true` first to see in the log what the job would select.

## Protecting your account

Fetchshelf will not start a download that would break these limits.

| Setting | Default | What it protects |
|---|---|---|
| `min_ratio` | `2` | Your upload/download ratio. A download that would push you below it is skipped. |
| `unsat_buffer` | `10` | Unsatisfied-snatch slots. MAM limits how many torrents you may have snatched but not yet fully seeded. Fetchshelf always leaves this many slots free. |
| `wedge_buffer` | `0` | Freeleech wedges. Fetchshelf never uses a wedge that would leave you with fewer than this many, so you always keep at least this many for your own use. This also applies to **Wedge download** on the Search page. |

Each autograb with a `name` can override `unsat_buffer` and `wedge_buffer` for itself. The values are
checked when a torrent is downloaded, not when it is queued, so after you change them and restart, they also
apply to torrents already waiting on the **Selected** page.

How a run works out how much it may download:

- **Slots:** your unsatisfied limit, minus the torrents already unsatisfied, minus the buffer. That is the
  most torrents one run may select.
- **Ratio:** free torrents cost nothing. For the others, the size must fit within
  `uploaded / min_ratio − downloaded − data still being downloaded`: the most that can be downloaded while
  the ratio stays at `min_ratio`. With 100 GB uploaded, 40 GB downloaded and `min_ratio = 2`, that is
  10 GB. An account at or below `min_ratio` gets no ratio budget at all. The Download job logs the budget
  at the start of every run.
- **Wedges:** a wedge is used only while more than `wedge_buffer` remain, so you never drop below it.

When a limit is reached, the remaining torrents stay selected on the **Selected** page and are tried on the
next run.

Other safeguards:

- `max_active_downloads` caps unfinished downloads per autograb.
- `ignore_torrents = ["123456"]` lists MAM ids that must never be selected.
- `add_torrents_stopped = true` adds torrents to qBittorrent in the stopped state, so you can review them
  before they start.
- Fetchshelf only removes files it linked itself, and never overwrites another torrent's files. Unlink, Clean
  and Relink leave a file in place when another book in the same folder also lists it.

Suggested starting point for a new setup: `min_ratio = 2`, `unsat_buffer = 10` or more,
`wedge_buffer = 5`, `cost = "free"` on your first autograb, and `dry_run = true` until the log looks right.

## How finished downloads are organized

Every `link_interval` minutes (default 10), Fetchshelf looks for torrents in
qBittorrent that are 100% complete, finds the matching library and links the wanted files in.

- **Which files.** From each torrent Fetchshelf links the files of at most two formats: the best audio
  format and the best ebook format it contains, where best means earliest in your format lists. The audio
  format comes from `music_types` for Musicology, `radio_types` for Radio and `audio_types` for everything
  else; the ebook format always comes from `ebook_types`. With the default lists, a torrent with both
  `m4b` and `mp3` files gets only its `m4b` files linked, and an audiobook that comes with a PDF gets the
  PDF as well. Other files are left alone.
- **Folder names.** Under `library_dir`:

  | Book | Folder |
  |---|---|
  | In a series | `Author/Series/Series #1 - Title` |
  | Not in a series | `Author/Title` |
  | With an edition | the above plus `, 2nd Edition` |
  | Audiobook with a narrator | the above plus ` {Narrator}` |

  For example `Brandon Sanderson/Stormlight Archive/Stormlight Archive #1 - The Way of Kings {Michael Kramer}`.
  Set `exclude_narrator_in_library_dir = true` to leave the narrator out. The author is the first author.
  A torrent with no author cannot be linked and shows up on the **Errors** page, where **Edit** adds one.
  When MAM gives no author but the title ends in "by" and a name, such as *Exploitation Film Posters by Graham
  Marsh*, that name is taken as the author and removed from the title. Only a name of two to four capitalized
  words counts, so titles like *Stand by Me* or *Murder by the Book* are left alone.
- **Multi-disc audiobooks.** Files in sub-folders named like `CD 1`, `Disc 2` or `Disk 3` go into a
  `Disc N` folder. Other folder structure is flattened.
- **Files with the same name.** When flattening would give two files of one torrent the same name (compared
  ignoring case), their sub-folder names go in front: `Part 1/01.mp3` and `Part 2/01.mp3` become
  `Part 1 - 01.mp3` and `Part 2 - 01.mp3`. When a different file of the same name is already in the book's folder,
  such as another upload or another language of the same title, the new one is placed as `name (2).ext`. A file
  of the same size already there counts as the same file and is left as it is.
- **Interrupted copies.** A copy is written under a hidden temporary name and renamed when complete, so a copy
  cut off by a restart or a full disk never sits in the library looking complete.
- **Metadata files.** A `metadata.json` next to the files of every book, whatever its media type, and a
  `metadata.opf` in libraries that ask for one. See below.
- **Looking torrents up on MAM.** A torrent Fetchshelf doesn't know yet is looked up on MAM by its
  info-hash, to get its title, authors and series. A run makes at most 200 such lookups. Pointed at a
  qBittorrent that already holds thousands of books, Fetchshelf works through them over several runs,
  200 every `link_interval` minutes, while books it already knows keep being linked as usual.

### `metadata.json`

Every book folder gets a `metadata.json` file describing the book, built from MAM's information about the
torrent, unless its library sets `metadata_json = false`. It is plain JSON, so any program can read it: use it to search or catalogue your library, feed a
script, import into another app, or just look up what a folder is. Audiobookshelf reads it as it is. For
example:

```json
{
  "authors": [
    "Brandon Sanderson"
  ],
  "narrators": [],
  "series": [
    "Stormlight Archive #1"
  ],
  "title": "The Way of Kings",
  "subtitle": null,
  "description": "The first book of The Stormlight Archive.",
  "isbn": "9780765326355",
  "asin": null,
  "tags": [],
  "genres": [
    "Fantasy"
  ],
  "language": "English",
  "explicit": false,
  "abridged": false
}
```

| Field | Contents |
|---|---|
| `authors`, `narrators` | Lists of names. `narrators` is empty for anything that is not read aloud. |
| `series` | Each series with its number, such as `Stormlight Archive #1`. |
| `title`, `subtitle` | A title with a colon is split at the colon, unless the part before it is shorter than 4 characters. |
| `description` | The torrent's description on MAM. |
| `isbn`, `asin` | When MAM has them; otherwise `null`. |
| `genres` | The torrent's MAM category and genres, such as `Fantasy` or `Science Fiction`. |
| `tags` | `LGBT` when MAM flags the torrent so; otherwise empty. |
| `language` | The language, such as `English`. |
| `explicit`, `abridged` | From MAM's flags. |

### `metadata.opf`

Calibre keeps a `metadata.opf` next to each of its books, and imports one it finds next to a book. To get
one in every book folder of a library, set `metadata_opf = true` on that `[[library]]`. Leave out
`metadata.json` with `metadata_json = false` if no app of yours reads it. Set either at the top of the file
instead to change it for every library that doesn't set its own:

```toml
[[library]]
name = "Ebooks"
category = "ebooks"
library_dir = "/books/calibre-ingest"
metadata_opf = true
metadata_json = false
```

The file is an OPF 2.0 package with only metadata, the way Calibre writes it. Audiobookshelf reads it too.
For example:

```xml
<?xml version="1.0" encoding="utf-8"?>
<package unique-identifier="mam_id" version="2.0" xmlns="http://www.idpf.org/2007/opf">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
    <dc:identifier id="mam_id" opf:scheme="MAM">123456</dc:identifier>
    <dc:title>The Way of Kings</dc:title>
    <dc:creator opf:role="aut">Brandon Sanderson</dc:creator>
    <dc:creator opf:role="nrt">Michael Kramer</dc:creator>
    <dc:description>The first book of The Stormlight Archive.</dc:description>
    <dc:language>eng</dc:language>
    <dc:identifier opf:scheme="ISBN">9780765326355</dc:identifier>
    <dc:subject>Fantasy</dc:subject>
    <meta name="calibre:series" content="Stormlight Archive" />
    <meta name="calibre:series_index" content="1" />
  </metadata>
</package>
```

| Element | Contents |
|---|---|
| `dc:title` | The full title, subtitle included: Calibre has no separate subtitle. |
| `dc:creator` | The authors (`aut`), then the narrators (`nrt`). Calibre takes the authors; Audiobookshelf both. |
| `dc:description` | The torrent's description on MAM. Left out when there is none. |
| `dc:language` | The language as a three-letter ISO 639 code, such as `eng` or `deu`. Left out for MAM's *Other*. |
| `dc:identifier` | The MAM torrent id, and the ISBN and ASIN when MAM has them. Calibre shows them as `mam:`, `isbn:` and `asin:` identifiers. |
| `dc:subject` | The same genres as in `metadata.json`. Calibre shows them as tags. |
| `calibre:series`, `calibre:series_index` | Each series, with the first number of its entry. Calibre uses the first series. |

### When the metadata files are written and removed

The files are rewritten when you **Refresh and relink** a book, and removed with the book's other files when
a better copy replaces it or you **Unlink** it, unless another torrent still uses the same folder. With the
`no_link` method no files are placed, so no metadata files are written either. Changing `metadata_json` or
`metadata_opf` affects books linked from then on; to apply it to books already in the library, use **Relink
selected** on the **Torrents** page.

### Better formats replace worse ones

Your format lists (`audio_types`, `ebook_types`, `music_types` and `radio_types`) are in order of
preference. If two copies of the same book are linked, the one whose format comes earlier in the list is
kept, and ties go to the larger one. With `ebook_types = ["epub", "pdf"]`, an epub replaces a pdf of the
same book.

Two torrents count as **the same book** when their titles match, they have the same media type (ebooks and
comics count as one), language and edition, they share an author, and they share a narrator or both have none.
Titles are compared ignoring case, accents on Latin letters, punctuation, a leading *The*, *A* or *An*, and the
words *Volume* and *Vol*; titles in other scripts, such as Cyrillic, Greek, Arabic, Hebrew, Chinese, Japanese or
Korean, are compared letter for letter. Author and narrator names are compared ignoring case, spacing,
punctuation and accents, so *J. R. R. Tolkien* and *J.R.R. Tolkien* are the same author. The same rule decides
what a new download duplicates (see the **Duplicates** page).

The replaced copy has its linked files, its folder (if empty), `cover.jpg` and its metadata files removed, is
tagged or moved in qBittorrent according to `on_cleaned`, and is marked *replaced* on its torrent page. When the better copy
was linked into the same folder, `cover.jpg` and the metadata files stay, since they now belong to it. The
downloaded data itself is not deleted.

The Link job leaves a replaced torrent alone. To bring one back, for example after removing the better
copy, use **Refresh and relink** on its page or **Relink selected** on the **Torrents** page: it is linked
again and no longer marked replaced. If the better copy is still linked, the next Clean retires it again.

#### Finding better copies

A better copy is found by any search: an autograb, a Hardcover list, the Follow job or the Search page. Such a
download is logged as *selected as a better copy than the library's*. To look for better copies on purpose:

- **Could be upgraded**, under *Problems* on the **Torrents** page, lists the linked books whose format isn't
  the first in its list, such as an mp3 audiobook when `audio_types` starts with m4b. Each shows its format and
  the preferred one: *(mp3; m4b preferred)*. *Any problem* leaves them out; a worse format is no problem.
- A book's own page shows the same, with **Look for a better copy**: a Search page search for its title and
  first author.
- The **Upgrade** job, with an [`[upgrade]`](configuration.md#upgrade) section, searches MAM for each such book
  in turn and selects a better copy of the same book when one is there:

  ```toml
  [upgrade]
  libraries = ["Audiobooks"]   # optional: only these libraries
  ```

  It searches at most `searches_per_run` books per run (20), and each book at most once every
  `search_again_after` days (30), whether a better copy was found or not. The better copy gets the old copy's
  qBittorrent category when that is a library's, so it lands in the same library. Without `[[upgrade.grab]]`
  blocks it only takes free copies (VIP, freeleech or personal freeleech): a book already in the library is
  worth a better copy, but rarely worth ratio or a wedge. Add blocks with another `cost` to change that.

### Unlinking a book you don't want

To take a book out of your library while qBittorrent keeps seeding it, use **Unlink** on its torrent page,
or tick several on the **Torrents** page and choose **Unlink selected**. Fetchshelf then:

- removes the files it linked, `cover.jpg` and the metadata files (unless another torrent shares the folder),
  and the folder if that leaves it empty, and deletes the book's Audiobookshelf item;
- applies `on_unlinked` to the torrent in qBittorrent, if you set it;
- keeps its record, marked *unlinked*, so the book still counts as one you have and is not downloaded again;
- leaves it alone in later Link runs, whatever its category.

The downloaded data is not touched. To bring the book back, use **Refresh and relink** on its page or
**Relink selected** on the **Torrents** page. **Remove record** is not the same: Fetchshelf then forgets the
book, and the next Link run links it again if a library still accepts it.

### When the folder no longer matches

If a book's metadata changes, or you change the configuration, the folder where it should be can differ
from where it is. The torrent is then flagged in the *Mismatch* column on the **Torrents** page:

- **No library:** no library accepts the torrent any more. A torrent Fetchshelf knows but has never linked,
  such as one that was already in qBittorrent under another category, gets this flag too, and the log says
  which category it has. Give it a library's category with **Set category** and the next Link run links it.
- **Different library:** it now belongs in another library.
- **Different folder:** it belongs in the same library, but in a different folder.

Open it and choose **Refresh and relink**, or tick several on the **Torrents** page and choose **Relink
selected**.

**Refresh and relink** links a book again from the download in qBittorrent. It picks the library from the
torrent's current category, save path and tags, removes the files Fetchshelf placed before, and places the
wanted files in the folder the book's details now call for. It also puts back files that went missing from
the library, for example a folder deleted by hand. Details you edited by hand are kept; only their VIP
status follows MAM. Before touching any file it checks that the torrent is in qBittorrent, that a library
accepts it, and that its files are where qBittorrent says. If anything is missing it says so at the top of
the page and changes nothing.

**Refresh** on its own only updates the details from MAM, without moving files, and replaces details you
edited by hand with MAM's.

## Using the web interface

There is no login. See [Security and privacy](#security-and-privacy).

| Page | What it is for |
|---|---|
| **Search** (home) | Search MAM and download books by hand. |
| **Jobs** | Every background job, its last run, how long it took and what it did, when it runs next, and a **Run now** or **Cancel** button. Updates live. Each job's name opens its history. Above them, the account's main numbers; below them, the kinds of job that are not configured. |
| **Account** | The MAM account's numbers, and how much Fetchshelf may still download, select and spend. Only in the menu when `mam_id` is set. |
| **Selected** | The queue of torrents that are chosen but not yet started. |
| **Torrents** | Every torrent Fetchshelf knows. |
| **Series** | Every series in the library, with its missing books; with a `[follow]` section, buttons to follow, watch or ignore it. |
| **Following** | The followed series and authors and what was found for them. Only in the menu with a `[follow]` section, with a count of the finds to look at. |
| **Events** | A log of what happened: grabbed, linked, cleaned, unlinked, updated, removed from MAM. |
| **Errors** | Jobs that keep failing, and the torrents and books the Download, Link, Clean, snatchlist, Hardcover list and Audiobookshelf jobs couldn't handle. A count on its menu link says when something needs attention. |
| **Duplicates** | Torrents skipped because you already have or have selected the same book. |
| **Lists** | The books on your Hardcover lists and what was found for each. Only in the menu when a `[[hardcover_list]]` is configured. |
| **Config** | A read-only view of the active configuration, with secrets hidden, and a **Send a test message** button for each `[[notify]]` block. |

### Account

The numbers every safety rule works from, and what Fetchshelf works out from them. When nothing is being
downloaded, the answer is usually here.

The page shows what MAM said at its last read, with the time of that read. The autograb, Hardcover list,
snatchlist and Download jobs read the account on every run, so the page itself doesn't call MAM. **Refresh
from MAM** reads it again. Only one job talks to MAM at a time; if one is busy, Refresh waits a few seconds
and then says which job it is, leaving the numbers as they were. Until something has read the account after
a restart, the page only offers **Refresh from MAM**.

**From MAM:** the account's name and class, ratio, uploaded and downloaded, unsatisfied snatches in use out
of the limit, wedges, and bonus points. Below them, **Snatches** lists every kind of snatch MAM counts, with
MAM's own labels, the count and the size. Three kinds are shown first as warnings when there are any, with a
link to the account's snatch lists on MAM: *Not Seeding - H&R - Not Yet Satisfied* (hit-and-runs), *Not Seeding - Not
Yet Satisfied*, which turn into hit-and-runs if left, and *Important Tracker Errors*.

**What Fetchshelf works out from it**, each with its formula:

- **Ratio budget:** how much more ratio-cost downloading `min_ratio` allows: uploaded / `min_ratio` −
  downloaded − torrents started but not finished at a cost that spends ratio, which MAM doesn't count as
  downloaded yet. At 0, the ratio is at or below `min_ratio`, and only free torrents start.
- **Selection slots:** how many more torrents an autograb or Hardcover list may select: the unsatisfied-snatch
  limit − those in use − `unsat_buffer`. An autograb with its own `unsat_buffer` or `max_active_downloads`
  gets a line of its own.
- **Wedges to spend:** wedges − `wedge_buffer`, and a line for each autograb with its own `wedge_buffer`.
- **Queue:** how many torrents wait on the **Selected** page, and how many of them these numbers would let
  the Download job start. An estimate: the Download job also skips torrents that are already in qBittorrent
  or no longer free.

**Connection to MAM:** when MAM last answered, whether MAM can reach the torrent client, and the last time MAM
refused the session (HTTP 403) or the account (HTTP 406). After a 403, Fetchshelf stops calling MAM for 30
minutes, and after a 429 for a minute; the page says until when, and why. A 403 usually means `mam_id` was
invalidated or is locked to another IP address: fix `mam_id` and restart Fetchshelf to resume sooner. A 429
needs nothing: the calls resume by themselves.

The **Jobs** page shows the ratio, the unsatisfied snatches, the wedges and the ratio budget above the job
list, with the pause notice when MAM calls are paused, and a link here.

### Selected

Shows each queued torrent with its cost, category, tags, what chose it (an autograb or a manual
search), and when it was queued and started. You can remove entries, or change a queued torrent's cost,
category and tags before it starts. The cost can be changed to `Ratio`, `TryWedge` or `UseWedge` (the
`ratio`, `try_wedge` and `wedge` costs from the configuration); the free costs only record what MAM
reported, so they cannot be chosen by hand.

### Torrents

- **Search, sort and filter.** Search by title, author or series. Case and accents do not matter, and with
  several words each must appear somewhere in those, in any order. The newest torrents come first; click
  the **Title** heading to sort by title, and click it again to reverse the order. The dropdowns below the
  search box show only some of the torrents, and apply as soon as you choose:
  - *Status*: *Linked*, or *Not linked*, which covers the four kinds below. *Waiting to be linked* means the
    Link job hasn't linked it yet, because it is still downloading or no library accepts it (then it is also
    flagged *No library*). *Replaced* means the Clean job retired it for a better copy, and *Unlinked by
    you* that you unlinked it. *Metadata only* is a book recorded with `metadata_only_add`, which Fetchshelf
    never downloads.
  - *Library*: the `[[library]]` the torrent was linked into.
  - *qBittorrent category*: the torrent's category in qBittorrent, as of the last Link run or
    **Check qBittorrent**.
  - *Problems*: a library mismatch (see the *Mismatch* column), torrents **Check qBittorrent** found missing
    from qBittorrent, torrents removed from MAM (found by an autograb with
    [`mark_removed`](configuration.md#mark_removed)), or any of these.
  - *Media type* and *Language*. Clicking a media type or language in the list does the same.
  - *Author*, *Narrator* and *Series*: click an author or series name in the list, or an author, narrator
    or series on a torrent's page, to see only that author's, narrator's or series' torrents. The filter
    shows above the list with the name; its **×** drops it. Names are matched the way duplicates are, so
    "J.R.R. Tolkien" and "J. R. R. Tolkien" are one author, and "The Expanse" and "Expanse" one series.

  Searching, sorting and filters combine, and **Clear filters** removes all filters but keeps the search.
  Pages hold 25 torrents; choose 50, 100, 500 or *All* under *Per page* below the list. Your browser
  remembers the choice for this list. Use **First**, **Previous**, **Next** and **Last** to move between
  pages.
- **Columns.** Besides the metadata, *Client status* is the torrent's state in qBittorrent as of the last
  check, and *Mismatch* shows when a linked book's folder no longer matches your library settings (see
  [When the folder no longer matches](#when-the-folder-no-longer-matches)). An unlinked book has
  *(unlinked)* after its title, and one MAM no longer has *(removed from MAM)* after its client status.
  **Edit** on each row opens the metadata editor.
- **Bulk actions.** Tick rows, then **Clean selected**, **Refresh selected**, **Relink selected**, **Unlink
  selected** or **Remove selected**.
  - *Clean* looks for another linked copy of the same book and retires the worse of the two, which can be
    the other copy (see [Better formats replace worse ones](#better-formats-replace-worse-ones)).
    Retiring removes that copy from your library: the hardlinks, copies or symlinks Fetchshelf made, its
    `cover.jpg` and metadata files (unless the kept copy shares the folder), and its folder if that is then
    empty. The downloaded data in qBittorrent is left alone. If no other copy of the book is linked, *Clean*
    does nothing. The Clean job does the same automatically after every link run.
  - *Refresh* re-reads its metadata from MAM.
  - *Relink* does what **Refresh and relink** does on a torrent's page, but with the details Fetchshelf already
    has, without asking MAM. That is what you want after a configuration change, or after **Refresh** or
    **Edit**. It then says above the list how many were relinked and why any were not. One that can't be
    relinked doesn't stop the rest. For fresh details from MAM as well, use **Refresh selected** first.
  - *Unlink* takes the books out of your library and keeps qBittorrent seeding them (see
    [Unlinking a book you don't want](#unlinking-a-book-you-dont-want)). It asks you to confirm first.
  - *Remove* deletes Fetchshelf's record (not the files).
  - **Set category**, below the other buttons, gives the ticked torrents one of the qBittorrent categories
    your `[[library]]` blocks use, chosen from the dropdown. That is how a torrent added outside Fetchshelf, or
    under another category, gets linked: the next Link run picks it up. Moving an already linked torrent to
    another library's category marks it with a library mismatch; **Relink selected** then moves it. In
    qBittorrent, a torrent in Automatic Torrent Management mode also moves its files to the new category's
    save path.
- **Check qBittorrent.** Updates the *Client status* column for every torrent. Books recorded with
  `metadata_only_add` are skipped: Fetchshelf never downloaded them, so it has no info hash to look up.
- **Torrent page.** Click a title for the full details: authors, narrators, series, language, media type,
  where the metadata came from, the library folder and linked files, the qBittorrent state, links to the
  torrent on MAM and, once the book is matched there, in Audiobookshelf, and the torrent's events. Notices
  show a library mismatch, an unlinked book, a book MAM no longer has (with the date that was found; the MAM
  link stays, as MAM may say why) and, for a replaced book, a link to the torrent that replaced it. Actions: **Clean**, **Refresh**, **Refresh and relink**, **Unlink**, **Start** and **Stop** in
  qBittorrent, **Remove from qBittorrent with data**, **Remove record**, and, for a replaced book, **Clear
  replaced marker**. While qBittorrent has the torrent, you can also change its category and tags there:
  choose one of your libraries' categories, untick a tag to remove it, type new ones separated by commas,
  and press **Set category and tags**. A category no library uses is only offered when the torrent already
  has it.
- **Edit.** If MAM's metadata is wrong, **Edit** lets you correct the title, authors, narrators, series
  (one `Name #number` per line), language, category and flags. Everything else MAM said about the torrent,
  such as its description, ISBN and file types, is kept. A hand-edited torrent keeps your values
  through link runs, snatchlists and **Refresh and relink**; only **Refresh** replaces them with MAM's
  details.

### Events

An audit log, newest first. Each entry says what happened to which torrent and, for metadata changes, which
fields changed. Events are never edited, so you can use them to see why something happened. Filter the list
by event type, by the autograb that grabbed a torrent, by the library that linked it, or by a
changed field. Pages hold 25 events; choose 50, 100, 500 or *All* under *Per page* below the list. Your
browser remembers the choice for this list. Use **First**, **Previous**, **Next** and **Last** to move
between pages.

### Errors

**Jobs that are failing** come first: every job whose latest run failed, with that run's error, since when it
has been failing and how many runs in a row, a link to its history, and **Run now**. A job leaves the list as
soon as a run succeeds, finishes with errors (it did its work) or is cancelled; there is nothing to remove.
When MAM refuses `mam_id` (HTTP 403) or blocks the account (HTTP 406), every job that talks to MAM fails at
once; those jobs share one line, "MAM refused `mam_id` (HTTP 403) since …", naming them and saying what to do.

Below them, one entry per torrent or book and job, showing the job that ran into the problem, the torrent or
book, the error and when it last failed. Every job tries a failed item again on its next run: the entry is
updated while it keeps failing and disappears once the item succeeds. The jobs that record entries:

- **Download**, **Link** and **Clean**, described below.
- **Snatchlist**: a snatch list row that couldn't be imported, by MAM id. It links to the torrent when
  Fetchshelf has it.
- **Hardcover list**: a book a list couldn't search MAM for, linking to its list. A refusal from MAM that
  stops the whole run is a failing job instead, not an entry per book.
- **Audiobookshelf**: a linked torrent that couldn't be matched to an Audiobookshelf item, such as one with no
  author or in a folder no Audiobookshelf library contains. A book Audiobookshelf hasn't scanned yet is not
  an error and gets no entry.

The list can be narrowed to one job. The Link job tries a failed torrent again on every run: the
entry is updated while it keeps failing and disappears once it links. The exception is *MAM has no torrent
with this info-hash*: such a torrent is usually from another tracker or was removed from MAM, so it is
looked up again only once a day. Remove its entry to have the next run look it up straight away. A queued download whose MAM link
went stale, and couldn't be refreshed, is taken out of the queue instead; **Retry download** puts it back on the
**Selected** page. One that MAM no longer has (deleted or merged) is taken out with that reason and has no
**Retry download**. A download that failed because MAM couldn't be reached stays queued for the next run and gets
no entry here; the log says why.
A torrent that couldn't be linked because MAM gives it no author, and that Fetchshelf has no record of yet,
has **Edit**: it opens the metadata editor filled in with MAM's details, and saving with an author added
creates the record. The next Link run then links it.
Tick the entries you have dealt with and use **Remove selected**.

The **Errors** link in the menu carries a count when something needs attention: the failing jobs, and the
entries recorded or updated in the last day, such as "Errors 3", with what it counts on hover. Older entries
stay on the page but don't keep the count lit.

### Duplicates

Torrents that an autograb or manual download skipped because you already have, or have already
selected, the same book in an equal or better format. A selection that was dropped for a better copy is
listed here too. Each entry links to the skipped torrent on MAM and shows what it duplicates: the torrent's
page when Fetchshelf has it, otherwise the queued selection or the torrent on MAM. Tick the entries you no
longer need and use **Remove selected**.

### Lists

One row per `[[hardcover_list]]`: how many books it has, how many still miss a format, and when it was last
read from Hardcover. Lists removed from the configuration are shown below, with what was found for them.

A list's page shows its books in Hardcover's order. For each format the list looks for, it says what
Fetchshelf found: *In the library* (linked to the torrent), *Selected for download*, *Not found on MAM*,
*On MAM, but it costs more than the list's grab blocks allow*, *Not wanted* (the list prefers the other
format) or *Not searched for yet*. Every book has a **Search MAM** link, a link to the book on Hardcover,
and **Mark done**, which stops Fetchshelf searching for it; **Undo done** takes that back. Show only the
books missing a format with the dropdown, and tick **Include books no longer on the list** to see those too.

### Config

Shows the Fetchshelf version and the settings in effect. For each autograb there is a **Run now** button that
runs that block once. For each `[[tag]]` rule there is **Apply to existing torrents**, which sets the rule's
category and tags on torrents you already have. For each `[[notify]]` block there is **Send a test message**.

## Background jobs

Fetchshelf does its work in jobs, listed on the **Jobs** page.

| Job | What it does | Runs |
|---|---|---|
| Autograb (one per `[[autograb]]`) | Searches MAM and selects what qualifies. | Every `search_interval` minutes, or the block's own interval. |
| Snatchlist (one per `[[snatchlist]]`) | Reads your snatch list, refreshes stored torrents and, with `metadata_only_add`, records the rest as owned books. | Same. |
| Hardcover list (one per `[[hardcover_list]]`) | Reads the list from Hardcover, searches MAM for the books still missing a format and selects what qualifies. | Same. |
| Download | Starts selected torrents in qBittorrent within your limits. | As soon as an autograb, a Hardcover list or the Search page selects something (unless `download_after_selection = false`), and every `search_interval` minutes. If it is already running when something is selected, it runs once more straight after, so the new selection isn't left for the next interval. |
| Link | Links finished torrents into libraries. | Every `link_interval` minutes. |
| Clean | Retires lesser formats. | After each link run. |
| Audiobookshelf | Matches linked books to Audiobookshelf items. | Every `[audiobookshelf]` `interval` minutes. |

Autograb, snatchlist and Hardcover list jobs are named after the block's `name`, or `autograb[0]`,
`snatchlist[1]`, `hardcover_list[0]` and so on if it has none. Give your blocks names so the Jobs page is
easy to read. Below the jobs, the page lists each kind of optional job you have not configured, such as
*Snatchlist is not configured*.

- **Run now** starts a job immediately. This works for every job, whatever its interval.
- **Cancel** replaces **Run now** while a job runs, and stops it. It asks for no confirmation, because
  nothing is lost that the next run doesn't redo: each job stops only where nothing is left half done.
  Download stops before a torrent's file is fetched, when nothing has been spent; a torrent whose file was
  fetched, perhaps with a wedge, is added to qBittorrent first. If adding it hangs for more than five
  seconds after the cancel, it is given up, stays queued, and the log says whether a wedge may have been
  spent on it. The same applies when Fetchshelf stops. Link finishes placing the torrent it is on and keeps everything it linked.
  Clean finishes retiring the copy it is on. Autograb, snatchlist and Hardcover list jobs stop between MAM
  requests, and Audiobookshelf between books. Cancelling a job doesn't cancel a job it started, such as
  the Clean run after Link or a Download run after an autograb: cancel that one on its own. A cancelled
  job runs again at its next interval. **Cancel** stops only the run it was shown for: if that run has
  ended and another started before the page caught up, the new run carries on.
- An interval of `0` means the job never runs by itself.
- Only one job talks to MAM at a time. If you press **Run now** while another is running, it waits its
  turn.
- The *last result* shows what the run did, such as *Success: 3 torrents linked* or *Success: 312 search
  results, 2 selected*, or the error. Hover over it for the numbers behind it. It is empty while the job runs, until
  that run ends. A failed job tries again at
  its next interval; while its runs keep failing, its row is shown in red with since when and how many runs
  in a row, and it is listed on the **Errors** page. One failing torrent never stops the others: the
  Download, Link, Clean, Audiobookshelf, snatchlist and Hardcover list jobs then show *Finished with errors*
  with the number of torrents or books that failed, and each is listed on the **Errors** page, except a
  download that failed because MAM couldn't be reached, which stays queued for the next run.
- With an empty `mam_id`, no job runs at all.

Selecting a job's name opens its **history**: its runs, newest first, 50 to a page, each with when it
started, how long it took, what started it (*Schedule*, *Startup*, *Manual* or *Another job*), its result and
its summary. Hover over a summary for its numbers. **Only runs that didn't succeed** leaves out the
successful ones. The newest 200 runs of each job are kept.

### When a job first runs after a restart

A job whose last run ended less than one interval ago keeps its rhythm: it next runs one interval after
that run ended, as it would have without the restart. A run the restart cut off has no known end, so one
interval after its start counts instead, and a container that keeps restarting can't make a job run more
often than its interval.

A job that has never run, or is overdue, runs soon after start, one after another so they don't all reach
MAM at once: Link (and Audiobookshelf) after 30 seconds, then each autograb, snatchlist and Hardcover list in
the order of the configuration, 30 seconds apart, and the Download job after the last of them. The history
shows those runs as started by *Startup*, and *Next run in* on the Jobs page shows when each job will run.

## Getting notified

Fetchshelf runs in the background and is checked rarely, so a problem can sit unnoticed for days. It can send
a message to a phone or a chat when something needs attention. Add a `[[notify]]` block to `config.toml` for
each place to send to, and list the events wanted (see the
[reference](configuration.md#notify) for every setting, and one example block for each of ntfy, Gotify,
Discord, Apprise and a generic webhook):

```toml
web_url = "https://fetchshelf.example.net"      # optional: what messages link to

[[notify]]
name = "Phone"
url = "https://ntfy.sh/fetchshelf-x7k2"
format = "ntfy"
events = ["mam_refused", "job_failing", "job_recovered", "grabbed"]
```

What is worth sending depends on how much attention is wanted. Starting with `mam_refused`, `job_failing` and
`job_recovered` covers the cases where Fetchshelf stops working and nothing says so; `grabbed` and `linked`
are for following along.

- **Only changes are sent.** A job that fails every 10 minutes for a day sends one message when it starts
  failing and one when it works again.
- **A refused `mam_id` is one message,** not one per job it stopped: the message says until when MAM calls are
  paused and what to do. A block that wants it doesn't also get a failure for each job.
- **Bursts are merged.** Grabs and links within 30 seconds of each other are one message: "Grabbed 4
  torrents: *A*, *B*, *C* and 1 more."
- **Hit-and-runs.** `hit_and_run` is sent when MAM counts more hit-and-runs than the last count Fetchshelf saw.
  The count is kept across restarts, so one that appears while Fetchshelf is stopped is still reported. The
  first time a block asks for the event, a hit-and-run that already exists is reported once.
- **Trying it.** The **Config** page lists each block, with the URL hidden, and **Send a test message**, which
  sends to that block straight away and shows the HTTP status or what went wrong. `--check-config` lists the
  blocks too, with the URLs hidden.
- **When delivery fails,** the message is tried once more after 10 seconds, then dropped, with a warning in
  the log. Nothing waits for the network: a service that is down never slows a job, nor another block's
  messages. Messages are not stored, so those still waiting when Fetchshelf stops get 5 seconds to be sent,
  and are lost if that is not enough; the log says how many.
  The **Events** page and each job's history on the **Jobs** page have the same facts.
- **The URL is a secret.** A topic name or webhook id is all it takes to send to it, and for a public ntfy
  topic also to read what is sent. Choose a topic that is hard to guess, or keep the URL in a file with
  `url_file`.

## Common setups

The [numbered examples](config-examples/README.md) are complete configs for these.

| I want to... | Start with | Notes |
|---|---|---|
| Keep my own downloads tidy, nothing automatic | [`2-library-organizer.toml`](config-examples/2-library-organizer.toml) | Add libraries for each category you use. Optionally add `[audiobookshelf]`. |
| Download my bookmarked books automatically | [`3-downloader.toml`](config-examples/3-downloader.toml) | Check `min_ratio` and the buffers. Start with `dry_run = true`. |
| Follow an author or look for new content | [`4-targeted-searches.toml`](config-examples/4-targeted-searches.toml) | Use `search_in`, `max_authors` and a sensible `cost`. |
| See every available option | [`5-everything.toml`](config-examples/5-everything.toml) | Read it, do not copy it whole. |

Whichever you start from, work in this order: set `mam_id`, connect qBittorrent, get one library working
with a torrent you downloaded by hand, then add autograbs.

## Updating Fetchshelf

Before you update:

1. Read [RELEASES.md](../RELEASES.md). It lists what changed in each release and anything you must do when
   upgrading.
2. Back up your config and database folders (see [Backups](#backups-and-moving-to-a-new-machine)). Keep the
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

  Restoring the backup winds back only Fetchshelf's own records, in `data.db`, to the moment you took it.
  Everything outside it stays as it is: the downloads in qBittorrent, the files in your library,
  Audiobookshelf, and your MAM account, including any wedges spent. Fetchshelf catches up with most of it
  by itself:

  - **Downloads and library files.** The Link job finds the finished torrents in qBittorrent that it no
    longer knows and that one of your libraries accepts, looks them up on MAM and records them again. Files already in the library are kept as
    they are, not linked a second time.
  - **The download queue.** A torrent queued before the backup but started after it shows on the
    **Selected** page as waiting again. The next download run sees it is already in qBittorrent and
    marks it as started, so nothing is downloaded twice. It does fetch the torrent file from MAM again
    to recognise it, but MAM does not charge a second wedge for a torrent you already used one on.
  - **Lost for good:** events, errors and duplicates recorded after the backup, and metadata you edited by
    hand since then. A re-found torrent gets MAM's metadata again, so redo such edits and use **Refresh
    and relink**.
  - Torrents selected after the backup but not yet started are forgotten; an autograb that still matches
    them selects them again on its next run.
- To avoid surprise upgrades, use a version tag such as `ghcr.io/crimsonking60/fetchshelf:2026.10.2` instead of
  `latest`. To update, change the tag to the new version, then pull and recreate the container as above.
- After updating, open **Jobs** and check that the jobs run and finish without errors.

## Backups and moving to a new machine

What matters:

| What | Where | Why |
|---|---|---|
| `config.toml` | the `/config` folder | Your settings and secrets. |
| `data.db` | the `/database` folder | Everything Fetchshelf remembers: torrents, selections, events, errors. |
| `keys` | the `/database` folder | Keys that protect the web interface's forms. Not essential: without them, a page that was open before is refused once, and works again after a reload. |
| `instance-id` | the `/database` folder | A random name for this installation, used in the names of the browser cookies that protect its forms, so two Fetchshelf installations on one machine (on different ports) don't overwrite each other's cookies. Not essential: a new one is made if it is missing, and a page that was open before is refused once. |

Your library and downloads are separate; back them up the way you normally do. Fetchshelf's own data is
small.

To back up, **stop the container**, copy both folders, then start it again. A copy of `data.db` taken while
Fetchshelf writes to it can be unusable. With [`database_wal = true`](database-setup.md#database_wal), the
latest changes are also kept in a `data.db-wal` file next to `data.db` while Fetchshelf runs, so a copy
taken then can be missing them. When the container stops, they are written into `data.db` and the extra
files are removed. If `data.db-wal` or `data.db-shm` are still there after a stop, for example after a
crash, copy them as well and keep them next to `data.db`. See [Database setup](database-setup.md) for where
the database can safely live.

To move to another machine, install Fetchshelf there, stop it on both machines, copy `config.toml` and
everything in the database folder into the new config and database folders, and start it. Mount your media at the
**same paths** as before (or adjust `library_dir` and `path_mapping`), and use the same `PUID` / `PGID`, or
fix the ownership of the copied files.

## Security and privacy

- **There is no login.** Anyone who can open the web interface can start downloads, remove records and, with
  the torrent page actions, delete data from qBittorrent. Only expose Fetchshelf on a trusted network, or
  put it behind a reverse proxy that does authentication. Never publish port 3157 to the internet. The same
  goes for the OPDS catalog, which lets anyone who can reach it download your books.
- **Secrets in `config.toml`:** `mam_id` and the qBittorrent and Audiobookshelf credentials.
  Keep the file readable only by you, never post it unredacted, and never commit it to a public repository.
  The **Config** page hides secrets, but the file itself does not. With `mam_id_file`, the session id lives
  in its own file instead; protect that file the same way.
- **What leaves your machine.** Fetchshelf talks to MAM (searches, downloads, your account state),
  qBittorrent and Audiobookshelf (on your network). It sends no usage data anywhere else.
- **Logs.** At every level, logs leave out your `mam_id`, passwords, API tokens, session cookies and the
  full web addresses Fetchshelf requests. They do contain each job's start, finish and error message, the
  names of your autograbs and snatchlists, MAM torrent ids, and, when MAM refuses a request, the start of MAM's
  reply. At the `debug` level they also contain the titles of the torrents each job looks at, including
  every search result an autograb rejects; the default level, `information`, leaves those out, but a
  `dry_run` block logs its titles at any level. Error messages can name hosts, folders and files on your
  system. Read a log before you share it and remove anything you consider private.

## Troubleshooting

First, look at the **Jobs**, **Errors** and **Events** pages and at `docker logs fetchshelf`. A count on the
menu's **Errors** link means a job keeps failing or something failed in the last day.

### The container is unhealthy

Run `docker inspect --format '{{json .State.Health}}' fetchshelf` to see the last checks and their answers.
`Unhealthy` means Fetchshelf runs but cannot open its database: check the permissions on `/database` (see
below). *Connection refused* means Fetchshelf is not answering at all: read `docker logs fetchshelf`.

### The container exits or restarts

Run `docker logs fetchshelf`. If the message begins with *Invalid configuration*, fix the setting it names
(a misspelled key or name, a missing `url`, a library with both or neither of `category` and
`download_dir`, a number out of range, and so on) and restart. While the container keeps restarting,
`docker exec` can't reach it; check the file with a one-off container instead:
`docker run --rm --entrypoint dotnet -v /path/to/config:/config:ro ghcr.io/crimsonking60/fetchshelf /app/Fetchshelf.Web.dll --check-config`.

### Permission errors on `/config` or `/database`

Set `PUID` and `PGID` to the owner of the host folders, or change the owner of the folders to match. If you
use `--user` or `user:` instead, make sure that user can write to both folders.

### Nothing runs

An empty `mam_id` means no jobs run. Also check the interval: `0` means the job only runs when you press
**Run now**.

### MAM login fails, or "too many requests", or the account is blocked

*MAM refused the request (HTTP 403)* usually means the session has expired or does not match your address.
Fetchshelf then pauses its MAM requests for 30 minutes. Create a new session, update `mam_id` and restart,
which also ends the pause. For rate limiting, wait: the job tries again at its next interval. A blocked
account (HTTP 406) aborts downloads; wait and check your account on MAM.

A request that gets no answer from MAM (a dropped connection, or no reply within 30 seconds) or a temporary
server error (HTTP 502, 503 or 504) is tried once more after 2 seconds before the job gives up on it; the log
shows *trying again in 2 s*. Downloads that spend a wedge are not tried again, since the first attempt may already
have spent it.

### qBittorrent cannot be reached

Check the `url`, user name and password, and that the Web UI is enabled. In Docker, use the service or
container name rather than `localhost`, and make sure both containers share a network. Use
**Check qBittorrent** on the **Torrents** page to test.

### Linking fails or files do not appear

- *Source missing.* Fetchshelf cannot see the path qBittorrent reported. Add or fix `path_mapping`, or
  mount the same folder in both containers.
- *Cross-device link* (with `method = "hardlink"`). Hardlinks need the same filesystem, and in Docker the
  same mount: two separate mounts count as different devices even on one disk. Mount the folder that holds
  both downloads and library, or use the default `hardlink_or_copy`.
- *No library matched.* No library accepts the torrent's category, save path or tags. Check your `[[tag]]`
  rules and `[[library]]` entries.
- *No author.* The torrent has no author in its metadata. Use **Edit** to add one: on the torrent's row on the
  **Torrents** page, or on the **Errors** page for a torrent added in qBittorrent that Fetchshelf has no
  record of yet.
- *No wanted file type.* None of the torrent's files has an extension from the format list for its media
  type: `audio_types`, `ebook_types`, `music_types` or `radio_types`.

### Nothing is being downloaded

The usual reasons, in order:

1. The autograb has `cost = "free"` (the default), and what it finds is not free.
2. The filter is too narrow, or the search returns nothing. Run the block with `dry_run = true` and read the
   log.
3. The budget is used up: no unsatisfied slots, not enough ratio budget, or the wedge reserve. The
   torrents stay on the **Selected** page. The **Account** page shows each budget and what it is worked out
   from, and whether MAM calls are paused.
4. The book is already in your library, or a duplicate of one, in an equal or better format. Skipped
   torrents are listed on the **Duplicates** page.
5. The torrent has none of your wanted file types.
6. The block's interval is `0`.

### A book went into the wrong folder

Open its torrent page and use **Edit** to fix its metadata, then **Refresh and relink**, which keeps your
edits. Do not use **Refresh** afterwards: it replaces your edits with MAM's details. Remember that the
author used is the **first** author.

### Audiobookshelf does not find a book

Make sure an Audiobookshelf library contains the folder `library_dir` points to, at the same path as in
Fetchshelf or at the path `[audiobookshelf]` `path_mapping` maps it to, and that Audiobookshelf has scanned it. The matcher runs every `interval` minutes, or press
**Run now** on the **Audiobookshelf** job.

The log says what happened to each book:

- *Matched … to Audiobookshelf item …*: done.
- *… is not in Audiobookshelf yet* (a warning): the path fits an Audiobookshelf library, but the book is not
  there. Usually Audiobookshelf has not scanned it yet; the matcher tries again on its next run.
- *no Audiobookshelf library has a folder containing …* (an error): Audiobookshelf sees your library at a
  different path that no `path_mapping` maps, or the library is missing from Audiobookshelf. The path in the
  message is the one Fetchshelf expects Audiobookshelf to see, after `path_mapping`. The job then shows *Finished with errors*,
  and the book is listed on the **Errors** page.
- *Could not list the Audiobookshelf libraries* (an error): Fetchshelf cannot reach Audiobookshelf, or
  `token` is wrong. Check `url` and `token` in `[audiobookshelf]`.

### A Hardcover list fails

The job's *last result* on the **Jobs** page, and the log, say why:

- *Hardcover rejected the token: it is wrong or has expired*: the token was mistyped, revoked, or its expiry
  date has passed. Create a new one and restart Fetchshelf.
- *The Hardcover token lacks the … permission*: create a token with `read:me`, `read:library` and, for a
  `list`, `read:lists`.
- *You have no Hardcover list with the slug …*: check `list` against the last part of the list's address.
- *Hardcover's rate limit is used up*: Hardcover allows a limited number of requests a day. Run the list
  less often with `search_interval`.

When Hardcover can't be reached, the run fails before it asks MAM anything, so the other jobs carry on as
usual.

### Getting more detail

Set `FETCHSHELF_LOG=debug` (the default is `information`) and, for a file you can attach to a report, set
`FETCHSHELF_LOG_DIR=/database/logs` and restart. Set it back afterwards: `debug` logs a lot more.

## Frequently asked questions

**Does it work without Audiobookshelf?** Yes. Fetchshelf downloads and organizes every kind of book the same
way without it, and writes the same metadata files. Use any reader, player or other app you like
with the library; Audiobookshelf can still read the files if you add it later.

**Is it only for audiobooks?** No. It handles ebooks, comics and manga, periodicals, musicology and radio as
well. Audiobookshelf is just the one app it has a direct integration with.

**Can I use it without qBittorrent?** No. Both halves work through qBittorrent.

**Does it ever delete my downloaded data?** Clean and **Unlink** only remove files that Fetchshelf itself
linked, and empty folders, `cover.jpg`, `metadata.json` and `metadata.opf` in a book's own library folder. The one action that
deletes torrent data is **Remove from qBittorrent with data** on a torrent's page, which you click yourself
and then confirm.

**How do I stop it downloading?** There are two ways:

- **Right away, without changing the configuration:** stop the container with `docker stop fetchshelf` (or
  `docker compose stop fetchshelf`). While it is stopped, nothing new is sent to qBittorrent, and finished
  downloads are not linked either. Start it again with `docker start fetchshelf` (or
  `docker compose start fetchshelf`).
- **Keep Fetchshelf running, but download nothing on its own:** in `config.toml`, set
  `search_interval = 0`, set any `search_interval` on an individual `[[autograb]]` to `0` as well, and
  restart. Autograbs then run only when you press **Run now**, and linking carries on as before.

Either way, torrents already in qBittorrent keep downloading; pause them in qBittorrent if you want them to
stop too. To keep downloading but review each torrent before it starts, set `add_torrents_stopped = true`
instead: new torrents then arrive in qBittorrent stopped.

**How do I download only audiobooks, or only ebooks?** Set `media_type = ["Audiobook"]` (or `["Ebook"]`) on
your autograbs, or `categories = { audio = true, ebook = false }`.

**How do I avoid anthologies?** Use `max_authors`, for example `max_authors = 3`.

**How do I stop a job running on a schedule?** Set its `search_interval` (or `link_interval`) to `0` and
start it with **Run now**.

**Can I use more than one qBittorrent?** Yes, list several `[[qbittorrent]]` entries. New downloads go to
the first. Linking works against all of them.

**Can I prefer one format over another?** Yes, with the order of the format lists: `audio_types`,
`ebook_types`, `music_types` and `radio_types`.

**Do config changes need a restart?** Yes.

## Getting help

- **Questions and ideas:** ask in [Discussions](https://github.com/crimsonking60/fetchshelf/discussions):
  **Q&A** for questions about using Fetchshelf, **Ideas** for suggestions you'd like feedback on.
- **Bugs and feature requests:** [open an issue](https://github.com/crimsonking60/fetchshelf/issues/new/choose)
  with the **Bug report** or **Feature request** form. Search the existing issues first, including closed
  ones.
- **Security problems:** never in public. Use **Report a vulnerability** on the repository's **Security** tab;
  see the [security policy](../SECURITY.md).
- **Include in a report:** the Fetchshelf version (shown on the **Config** page, and in the first line of the
  log, `Fetchshelf 2026.10.2 starting`), what you expected and what happened, the relevant lines from the
  log, and your configuration with the secrets removed.
- **Remove secrets first:** replace `mam_id`, passwords and tokens with placeholders such as
  `replace-with-your-token`. Also check log lines for private paths and names.
