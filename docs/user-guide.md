# Fetchshelf user guide

> **Work in progress.** This guide is incomplete and may change.

This guide is for people who run Fetchshelf. It assumes you use Docker, optionally with Docker Compose,
are comfortable editing a text file, and know how MAM works: ratio, unsatisfied snatches, freeleech, wedges
and VIP are not explained here. It does not cover building Fetchshelf from source.

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

## What is Fetchshelf?

Fetchshelf is a self-hosted service that connects MAM with the BitTorrent client qBittorrent. It downloads
and organizes anything MAM carries: ebooks, audiobooks, comics and manga, periodicals, musicology and radio.
It runs in the background, has a web interface for looking at what it is doing, and is configured with a
single text file.

It has two halves that you can use together or separately:

- **The auto-downloader** finds torrents on MAM that match rules you write (your bookmarks, a favourite
  author, a Notion reading list, and so on) and adds them to qBittorrent. While doing so it protects your
  account: it keeps your ratio above a limit, leaves some unsatisfied-snatch slots free and holds back a
  number of freeleech wedges.
- **The library organizer** watches qBittorrent for finished torrents and links the files into a tidy
  `Author/Series/Title` folder tree. Next to every title it writes a `metadata.json` file with the
  authors, series, description, ISBN, genres and more, taken from MAM (see
  [`metadata.json`](#metadatajson)). When a better format of a title arrives, it retires the older copy.

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
  filesystem** and, in Docker, reach Fetchshelf through the **same mount**. Otherwise use the copy or
  symlink link methods (see
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
hardlink fails.

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
  container. Restarting is safe at any time: queued selections, known torrents, events and errors are kept
  in the database and picked up again after the restart. A job that the restart cuts off does its work on
  its next run, and a download that already reached qBittorrent is recognised there rather than added
  twice. Two things start fresh: the **Jobs** page forgets each job's last run, and each scheduled job
  waits one full interval before its first run. Use **Run now** if you don't want to wait.
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
- **Expired or invalid session.** If jobs fail with an authentication error, check the session id with the
  `curl` command above. If MAM does not accept it, create a new session, update `mam_id` (or the file
  `mam_id_file` points to) and restart.

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
| `hardlink` (default) | A second name for the same file. No extra space. Needs the same filesystem. | Downloads and library share a filesystem and you keep seeding. |
| `hardlink_or_copy` | Hardlink, falling back to a copy. | You are not sure the folders are on the same filesystem. |
| `hardlink_or_symlink` | Hardlink, falling back to a symbolic link. | As above, but you prefer links to copies. |
| `copy` | A full copy. | The library is on a different disk. Uses extra space. |
| `symlink` | A symbolic link to the download. | Rarely. Some programs cannot follow links. |
| `no_link` | Records metadata only; no files are placed. | You only want tracking. |

Seeding keeps working with hardlinks and symlinks, because the original files stay where qBittorrent put
them.

To use the same method for all your libraries, set it once at the top of the file instead of in each
`[[library]]`. A library's own `method` still wins:

```toml
link_method = "hardlink_or_copy"
```

## Connecting to Audiobookshelf

Skip this section if you do not use Audiobookshelf. It is the one app Fetchshelf talks to directly, but
the library and its `metadata.json` files are the same with or without it.

Even without this section, Audiobookshelf reads the `metadata.json` file that Fetchshelf writes next to
every book. With it, Fetchshelf also:

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

The **Search** page is also the home page. Use it to search MAM yourself and download a book by hand.

- **Search** looks for your text in the title and author of every torrent on MAM. With the box empty, the
  page shows the newest torrents on MAM instead. Either way you get at most the 100 newest matches; there
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
  of MAM, so they cannot bring in more results, and they are reset when you run a new search.
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
| `"newest_first"` (default) | Newest uploads first. |
| `"oldest_first"` | Oldest uploads first. Together with a higher `max_pages`, good for a one-off backfill. |
| `"random"` | A different random selection on every run. |
| `"low_seeders"` | Fewest seeders first: torrents that need seeding most. |
| `"low_snatches"` | Fewest completed downloads first. |

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
| `metadata_only` | Never download; just refresh the details of books Fetchshelf already has. |
| `metadata_only_add` | Like `metadata_only`, but also records the books Fetchshelf does not have yet, still without downloading them. Fetchshelf then treats those books as already in your library: it will not download them again unless it finds a better format. |

Torrents that are already free are always treated as free, so wedges are never wasted.

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

## Importing reading lists

Instead of searching, you can keep a list of the torrents you want and let Fetchshelf fetch them.

### Notion

A Notion database whose rows contain links to MAM torrents:

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

- `mam_fields` names the URL properties that hold MAM links. The number at the end of each link is the MAM
  torrent id. Ids Fetchshelf already knows are skipped.
- `grab` blocks decide what is acceptable and how to pay. For each linked torrent they are tried **in
  order, and the first block whose filter matches decides the cost**, so put the most specific one first.
  Every list needs at least one.
- Books you already have, in an equal or better format, are not downloaded again.

### Snatchlists

A `[[snatchlist]]` reads one of your MAM snatch lists without downloading anything. With
`cost = "metadata_only_add"` it records every torrent on the list as something you already own, which
stops Fetchshelf downloading a book you got some other way. With the default, `metadata_only`, it only
refreshes the details of torrents Fetchshelf already has. See
[`4-targeted-searches.toml`](config-examples/4-targeted-searches.toml).

The first `metadata_only_add` run on a long list looks every torrent up on MAM once, so it can take a while.
Later runs read the list and look up only what is new. Details you edited by hand on a torrent's page are
kept; a snatchlist only updates their VIP status.

## Protecting your account

Fetchshelf will not start a download that would break these limits.

| Setting | Default | What it protects |
|---|---|---|
| `min_ratio` | `2` | Your upload/download ratio. A download that would push you below it is skipped. |
| `unsat_buffer` | `10` | Unsatisfied-snatch slots. MAM limits how many torrents you may have snatched but not yet fully seeded. Fetchshelf always leaves this many slots free. |
| `wedge_buffer` | `0` | Freeleech wedges. Fetchshelf never uses a wedge that would leave you with fewer than this many, so you always keep at least this many for your own use. This also applies to **Wedge download** on the Search page. |

Each autograb and each list can override `unsat_buffer` and `wedge_buffer` for itself.

How a run works out how much it may download:

- **Slots:** your unsatisfied limit, minus the torrents already unsatisfied, minus the buffer. That is the
  most torrents one run may select.
- **Ratio:** free torrents cost nothing. For the others, the size must fit within
  `(uploaded − downloaded − data still being downloaded) / min_ratio`.
- **Wedges:** a wedge is used only while more than `wedge_buffer` remain, so you never drop below it.

When a limit is reached, the remaining torrents stay selected on the **Selected** page and are tried on the
next run.

Other safeguards:

- `max_active_downloads` caps unfinished downloads per autograb.
- `ignore_torrents = ["123456"]` lists MAM ids that must never be selected.
- `add_torrents_stopped = true` adds torrents to qBittorrent in the stopped state, so you can review them
  before they start.
- Fetchshelf only removes files it linked itself, and never overwrites another torrent's files.

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
  A torrent with no author cannot be linked and shows up on the **Errors** page.
- **Multi-disc audiobooks.** Files in sub-folders named like `CD 1`, `Disc 2` or `Disk 3` go into a
  `Disc N` folder. Other folder structure is flattened.
- **`metadata.json`.** Written next to the files of every book, whatever its media type. See below.

### `metadata.json`

Every book folder gets a `metadata.json` file describing the book, built from MAM's information about the
torrent. It is plain JSON, so any program can read it: use it to search or catalogue your library, feed a
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

The file is rewritten when you **Refresh and relink** a book, and removed with the book's other files when a
better copy replaces it. With the `no_link` method no files are placed, so no `metadata.json` is written
either.

### Better formats replace worse ones

Your format lists (`audio_types`, `ebook_types`, `music_types` and `radio_types`) are in order of
preference. If two copies of the same book are linked, the one whose format comes earlier in the list is
kept, and ties go to the larger one. With `ebook_types = ["epub", "pdf"]`, an epub replaces a pdf of the
same book.

The replaced copy has its linked files, its folder (if empty), `cover.jpg` and `metadata.json` removed, is
tagged or moved in qBittorrent according to `on_cleaned`, and is shown as *replaced*. The downloaded data
itself is not deleted. You can still relink a replaced torrent later.

### When the folder no longer matches

If a book's metadata changes, or you change the configuration, the folder where it should be can differ
from where it is. The torrent is then flagged on the **Torrents** page as `NoLibrary`, `NewLibraryDir` or
`NewPath`. Open it and choose **Refresh and relink**.

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
category and tags before it starts. The cost can be changed to `Ratio`, `TryWedge` or `UseWedge` (the
`ratio`, `try_wedge` and `wedge` costs from the configuration); the free costs only record what MAM
reported, so they cannot be chosen by hand.

### Torrents

- **Search, sort and filter.** Search by title, author or series. The newest torrents come first; click
  the **Title** heading to sort by title, and click it again to reverse the order. Click a torrent's media
  type or language to show only torrents of that kind. Searching, sorting and filtering combine; the
  filters in use are listed above the table, each with a **show all** link to remove it. Pages hold 25
  torrents. Use **First**, **Previous**, **Next** and **Last** below the list.
- **Columns.** Besides the metadata, *Client status* is the torrent's state in qBittorrent as of the last
  check, and *Mismatch* shows when a linked book's folder no longer matches your library settings (see
  [When the folder no longer matches](#when-the-folder-no-longer-matches)). **Edit** on each row opens the
  metadata editor.
- **Bulk actions.** Tick rows, then **Clean selected**, **Refresh selected** or **Remove selected**.
  - *Clean* looks for another linked copy of the same book and retires the worse of the two, which can be
    the other copy (see [Better formats replace worse ones](#better-formats-replace-worse-ones)).
    Retiring removes that copy from your library: the hardlinks, copies or symlinks Fetchshelf made, its
    `cover.jpg` and `metadata.json`, and its folder if that is then empty. The downloaded data in
    qBittorrent is left alone. If no other copy of the book is linked, *Clean* does nothing. The Clean job
    does the same automatically after every link run.
  - *Refresh* re-reads its metadata from MAM.
  - *Remove* deletes Fetchshelf's record (not the files).
- **Check qBittorrent.** Updates the *Client status* column for every torrent.
- **Torrent page.** Click a title for the full details: authors, narrators, series, language, media type,
  where the metadata came from, the library folder and linked files, the qBittorrent state, links to the
  torrent on MAM and, once the book is matched there, in Audiobookshelf, and the torrent's events. Notices
  show a library mismatch and, for a replaced book, a link to the torrent that replaced it. Actions:
  **Clean**, **Refresh**, **Refresh and relink**, **Start** and **Stop** in qBittorrent, **Remove from
  qBittorrent with data**, **Remove record**, and, for a replaced book, **Clear replaced marker**. While
  qBittorrent has the torrent, you can also change its category and tags there: untick a tag to remove it,
  type new ones separated by commas, and press **Set category and tags**.
- **Edit.** If MAM's metadata is wrong, **Edit** lets you correct the title, authors, narrators, series
  (one `Name #number` per line), language, category and flags. A hand-edited torrent keeps your values
  through link runs, snatchlists and **Refresh and relink**; only **Refresh** replaces them with MAM's
  details.

### Events

An audit log, newest first. Each entry says what happened to which torrent and, for metadata changes, which
fields changed. Events are never edited, so you can use them to see why something happened. Filter the list
by event type, by the autograb or list that grabbed a torrent, by the library that linked it, or by a
changed field.

### Errors

One entry per torrent and job, showing the job that ran into the problem (Download, Link or Clean), the
torrent, the error and when it last failed. The Link job tries a failed torrent again on every run: the
entry is updated while it keeps failing and disappears once it links. A queued download whose MAM link
went stale is taken out of the queue instead; **Retry download** puts it back on the **Selected** page.
Tick the entries you have dealt with and use **Remove selected**.

### Duplicates

Torrents that an autograb, list or manual download skipped because you already have, or have already
selected, the same book in an equal or better format. A selection that was dropped for a better copy is
listed here too. Each entry links to the skipped torrent on MAM and shows what it duplicates: the torrent's
page when Fetchshelf has it, otherwise the queued selection or the torrent on MAM. Tick the entries you no
longer need and use **Remove selected**.

### Config

Shows the Fetchshelf version and the settings in effect. For each autograb there is a **Run now** button that
runs that block once. For each `[[tag]]` rule there is **Apply to existing torrents**, which sets the rule's
category and tags on torrents you already have.

## Background jobs

Fetchshelf does its work in jobs, listed on the **Jobs** page.

| Job | What it does | Runs |
|---|---|---|
| Autograb (one per `[[autograb]]`) | Searches MAM and selects what qualifies. | Every `search_interval` minutes, or the block's own interval. |
| Snatchlist (one per `[[snatchlist]]`) | Reads your snatch list, refreshes stored torrents and, with `metadata_only_add`, records the rest as owned books. | Same. |
| Notion list (one per `[[notion_list]]`) | Looks up each linked torrent and selects what qualifies. | Every `import_interval` minutes, or the list's own interval. |
| Download | Starts selected torrents in qBittorrent within your limits. | After autograbs and list runs select something, and every `search_interval` minutes. |
| Link | Links finished torrents into libraries. | Every `link_interval` minutes. |
| Clean | Retires lesser formats. | After each link run. |
| AbsMatcher | Matches linked books to Audiobookshelf items. | Every `[audiobookshelf]` `interval` minutes. |

Autograb, snatchlist and list jobs are named after the block's `name`, or `autograb[0]`, `snatchlist[1]`
and so on if it has none. Give your blocks names so the Jobs page is easy to read. Below the jobs, the page
lists each kind of optional job you have not configured, such as *Snatchlist is not configured*.

- **Run now** starts a job immediately. This works for every job, whatever its interval.
- An interval of `0` means the job never runs by itself.
- Only one job talks to MAM at a time. If you press **Run now** while another is running, it waits its
  turn.
- The *last result* shows success or the error. A failed job tries again at its next interval. One failing
  torrent never stops the others: the Download, Link, Clean, AbsMatcher and snatchlist jobs then show
  *Finished with errors* with the number of torrents that failed, and the log has a line for each. Link and Clean problems,
  and downloads removed from the queue, are also listed on the **Errors** page.
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

Your library and downloads are separate; back them up the way you normally do. Fetchshelf's own data is
small.

To back up, **stop the container**, copy both folders, then start it again. While Fetchshelf runs, its
latest changes are kept in a `data.db-wal` file next to `data.db`, so a copy taken while it runs can be
missing them or be unusable. When the container stops, they are written into `data.db` and the extra
files are removed. If `data.db-wal` or `data.db-shm` are still there after a stop, for example after a
crash, copy them as well and keep them next to `data.db`.

To move to another machine, install Fetchshelf there, stop it on both machines, copy `config.toml` and
everything in the data folder into the new config and data folders, and start it. Mount your media at the
**same paths** as before (or adjust `library_dir` and `path_mapping`), and use the same `PUID` / `PGID`, or
fix the ownership of the copied files.

## Security and privacy

- **There is no login.** Anyone who can open the web interface can start downloads, remove records and, with
  the torrent page actions, delete data from qBittorrent. Only expose Fetchshelf on a trusted network, or
  put it behind a reverse proxy that does authentication. Never publish port 3157 to the internet.
- **Secrets in `config.toml`:** `mam_id`, qBittorrent and Audiobookshelf credentials and a Notion token.
  Keep the file readable only by you, never post it unredacted, and never commit it to a public repository.
  The **Config** page hides secrets, but the file itself does not. With `mam_id_file`, the session id lives
  in its own file instead; protect that file the same way.
- **What leaves your machine.** Fetchshelf talks to MAM (searches, downloads, your account state),
  qBittorrent and Audiobookshelf (on your network), and, if you use it, Notion. It sends no usage data anywhere
  else.
- **Logs.** At every level, logs leave out your `mam_id`, passwords, API tokens, session cookies and the
  full web addresses Fetchshelf requests. They do contain each job's start, finish and error message, the
  names of your autograbs and lists, MAM torrent ids, and, when MAM refuses a request, the start of MAM's
  reply. At the `debug` level they also contain the titles of the torrents each job looks at, including
  every search result an autograb rejects; the default level, `information`, leaves those out, but a
  `dry_run` block logs its titles at any level. Error messages can name hosts, folders and files on your
  system. Read a log before you share it and remove anything you consider private.

## Troubleshooting

First, look at the **Jobs**, **Errors** and **Events** pages and at `docker logs fetchshelf`.

### The container is unhealthy

Run `docker inspect --format '{{json .State.Health}}' fetchshelf` to see the last checks and their answers.
`Unhealthy` means Fetchshelf runs but cannot open its database: check the permissions on `/database` (see
below). *Connection refused* means Fetchshelf is not answering at all: read `docker logs fetchshelf`.

### The container exits or restarts

Run `docker logs fetchshelf`. If the message begins with *Invalid configuration*, fix the setting it names
(a misspelled key or name, a missing `url`, a library with both or neither of `category` and
`download_dir`, and so on) and restart.

### Permission errors on `/config` or `/database`

Set `PUID` and `PGID` to the owner of the host folders, or change the owner of the folders to match. If you
use `--user` or `user:` instead, make sure that user can write to both folders.

### Nothing runs

An empty `mam_id` means no jobs run. Also check the interval: `0` means the job only runs when you press
**Run now**.

### MAM login fails, or "too many requests", or the account is blocked

An authentication error means the session has expired or does not match your address. Create a new session
and update `mam_id`. For rate limiting, wait: the job tries again at its next interval. A blocked account
(HTTP 406) aborts downloads; wait and check your account on MAM.

### qBittorrent cannot be reached

Check the `url`, user name and password, and that the Web UI is enabled. In Docker, use the service or
container name rather than `localhost`, and make sure both containers share a network. Use
**Check qBittorrent** on the **Torrents** page to test.

### Linking fails or files do not appear

- *Source missing.* Fetchshelf cannot see the path qBittorrent reported. Add or fix `path_mapping`, or
  mount the same folder in both containers.
- *Cross-device link.* Hardlinks need the same filesystem, and in Docker the same mount: two separate
  mounts count as different devices even on one disk. Mount the folder that holds both downloads and
  library, or use `hardlink_or_copy`.
- *No library matched.* The torrent has no category your libraries accept. Check your `[[tag]]` rules and
  `[[library]]` entries.
- *No author.* The torrent has no author in its metadata. Use **Edit** to add one.
- *No wanted file type.* None of the torrent's files has an extension from the format list for its media
  type: `audio_types`, `ebook_types`, `music_types` or `radio_types`.

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

Open its torrent page and use **Edit** to fix its metadata, then **Refresh and relink**, which keeps your
edits. Do not use **Refresh** afterwards: it replaces your edits with MAM's details. Remember that the
author used is the **first** author.

### Audiobookshelf does not find a book

Make sure an Audiobookshelf library contains the folder `library_dir` points to, at the same path as in
Fetchshelf, and that Audiobookshelf has scanned it. The matcher runs every `interval` minutes, or press
**Run now** on the **AbsMatcher** job.

The log says what happened to each book:

- *Matched … to Audiobookshelf item …*: done.
- *… is not in Audiobookshelf yet* (a warning): the path fits an Audiobookshelf library, but the book is not
  there. Usually Audiobookshelf has not scanned it yet; the matcher tries again on its next run.
- *no Audiobookshelf library has a folder containing …* (an error): Audiobookshelf sees your library at a
  different path, or the library is missing from Audiobookshelf. The job then shows *Finished with errors*.
- *Could not list the Audiobookshelf libraries* (an error): Fetchshelf cannot reach Audiobookshelf, or
  `token` is wrong. Check `url` and `token` in `[audiobookshelf]`.

### Getting more detail

Set `FETCHSHELF_LOG=debug` (the default is `information`) and, for a file you can attach to a report, set
`FETCHSHELF_LOG_DIR=/database/logs` and restart. Set it back afterwards: `debug` logs a lot more.

## Frequently asked questions

**Does it work without Audiobookshelf?** Yes. Fetchshelf downloads and organizes every kind of book the same
way without it, and writes the same `metadata.json` files. Use any reader, player or other app you like
with the library; Audiobookshelf can still read the files if you add it later.

**Is it only for audiobooks?** No. It handles ebooks, comics and manga, periodicals, musicology and radio as
well. Audiobookshelf is just the one app it has a direct integration with.

**Can I use it without qBittorrent?** No. Both halves work through qBittorrent.

**Does it ever delete my downloaded data?** The cleaner only removes files that Fetchshelf itself linked,
and empty folders, `cover.jpg` and `metadata.json` in a book's own library folder. The one action that
deletes torrent data is **Remove from qBittorrent with data** on a torrent's page, which you click yourself
and then confirm.

**How do I stop it downloading?** There are two ways:

- **Right away, without changing the configuration:** stop the container with `docker stop fetchshelf` (or
  `docker compose stop fetchshelf`). While it is stopped, nothing new is sent to qBittorrent, and finished
  downloads are not linked either. Start it again with `docker start fetchshelf` (or
  `docker compose start fetchshelf`).
- **Keep Fetchshelf running, but download nothing on its own:** in `config.toml`, set
  `search_interval = 0` and `import_interval = 0`, set any `search_interval` on an individual
  `[[autograb]]` or `[[notion_list]]` to `0` as well, and restart. Autograbs and lists then run only when
  you press **Run now**, and linking carries on as before.

Either way, torrents already in qBittorrent keep downloading; pause them in qBittorrent if you want them to
stop too. To keep downloading but review each torrent before it starts, set `add_torrents_stopped = true`
instead: new torrents then arrive in qBittorrent stopped.

**How do I download only audiobooks, or only ebooks?** Set `media_type = ["Audiobook"]` (or `["Ebook"]`) on
your autograbs, or `categories = { audio = true, ebook = false }`.

**How do I avoid anthologies?** Use `max_authors`, for example `max_authors = 3`.

**How do I stop a job running on a schedule?** Set its `search_interval` (or `link_interval`,
`import_interval`) to `0` and start it with **Run now**.

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
