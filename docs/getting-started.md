# Getting started

This page takes you from nothing to a working Fetchshelf: installed with Docker, connected to MAM and
qBittorrent, downloading your free bookmarks by itself and sorting finished books into a tidy library. Set
aside an hour for it. Everything you set up here can be changed later without starting over.

It assumes you know how MAM works (ratio, unsatisfied snatches, freeleech, wedges and VIP), that qBittorrent
already runs on your PC or NAS, and that you can edit a text file and type a command you are given. You don't
need to be a developer.

<!-- github-only:start -->
> [!NOTE]
> Development happens on `main`, so this page may describe changes that are not released yet. For the version you
> run, use the documentation linked under [Latest release](../README.md#latest-release) in the README.
<!-- github-only:end -->

## What Fetchshelf does

Fetchshelf runs next to qBittorrent and does two jobs for you:

- **It downloads.** Every half hour it looks at your bookmarks on MAM and has qBittorrent download the ones that
  are free (freeleech or VIP). It never starts a download that would take your ratio, unsatisfied-snatch slots or
  wedges past limits you set.
- **It organizes.** When a download finishes, Fetchshelf puts the book into a library folder, sorted as
  `Author/Series/Title`, without moving the download: qBittorrent keeps seeding it. Any app that reads a folder
  of books, such as Audiobookshelf for audiobooks, can use the library.

It has a web page where you see what it is doing, and where you can search MAM and download a book by hand.

Fetchshelf can do a lot more, from following series to keeping Audiobookshelf up to date. This page leaves all
of that out; [What else Fetchshelf can do](#what-else-fetchshelf-can-do) at the end gives you a tour.

## What you need

- **A MAM account in good standing.** Fetchshelf acts on your behalf, and you remain responsible for following
  MAM's rules.
- **qBittorrent with its Web UI turned on** (in qBittorrent: **Options → Web UI**). MAM only allows certain
  client versions, so check MAM's list of approved clients for yours.
- **Docker, with Docker Compose.** To check, run `docker compose version`; it prints a version number when
  Compose is there. Most NAS systems run Docker through an app of their own (such as Container Manager or
  Container Station), which can usually take the compose file this page gives you.
- **Fetchshelf on the same machine as qBittorrent's downloads.** It has to reach the downloaded files to put
  them in your library.

## Step 1: plan your folders

This is the one part to get right before you install, because it decides whether your library takes up any
extra disk space.

Fetchshelf puts a finished book into your library as a **hardlink**: the same file in two places, taking the
space only once. qBittorrent keeps seeding the file where it is, and your library shows it under a tidy name. A
hardlink only works when both places are on the **same disk** and, with Docker, reach Fetchshelf through **one
mount** (one `-v` or `volumes:` line). If they don't, Fetchshelf copies the files instead, which takes as much
space again.

The simplest way to get it right is one shared folder that holds both the downloads and the library:

```
/srv/media
├── downloads     qBittorrent saves here
└── library       Fetchshelf puts the books here
```

`/srv/media` is only an example; use any folder on the disk where your downloads go. Create the folders now, as
your own user, so they belong to you rather than to the system:

```sh
mkdir -p /srv/media/downloads /srv/media/library
mkdir -p /srv/fetchshelf/config /srv/fetchshelf/database
```

The second line makes the folders where Fetchshelf keeps its settings and its database.

### Where does qBittorrent save its downloads?

Open qBittorrent's Web UI, then **Options → Downloads**. **Default Save Path** is where downloads go. Fetchshelf
does not choose where a download is saved, so this setting decides it. The path shown is as qBittorrent sees it:
inside its container when it runs in Docker, on the machine itself when it doesn't.

There are two ways to go from here:

- **Make qBittorrent save into the shared folder (recommended).** If qBittorrent runs in Docker, give it the
  downloads folder at the same path that Fetchshelf will see it at, `/media/downloads`:

  ```yaml
      volumes:
        - /srv/media/downloads:/media/downloads
  ```

  and set **Default Save Path** to `/media/downloads`. Fetchshelf mounts the whole shared folder at `/media`
  (step 2), so both see a download at the same path, and nothing needs translating. If you turn on **Keep
  incomplete torrents in**, keep that folder under `/media/downloads` too.

- **Keep qBittorrent as it is.** Then the path qBittorrent reports for a download isn't one Fetchshelf can
  open, and you tell Fetchshelf how to translate it with `path_mapping` (in
  [step 4](#2-qbittorrent)). The library still has to be on the same disk as the downloads for hardlinks. For
  example, if qBittorrent saves to `/mnt/disk1/torrents` on the machine (or to `/downloads` inside its
  container, where `/downloads` is `/mnt/disk1/torrents` on the machine), make the library
  `/mnt/disk1/library`, and give Fetchshelf `/mnt/disk1` as its shared folder in step 2 instead of `/srv/media`.

## Step 2: install Fetchshelf

Fetchshelf is installed with a `docker-compose.yaml` file. Which one depends on how you run qBittorrent.

### If qBittorrent runs in Docker Compose

Add Fetchshelf to the same compose file, next to qBittorrent. Then Fetchshelf can reach qBittorrent by its
service name. A complete file, with qBittorrent set up as in step 1, looks like this:

```yaml
services:
  qbittorrent:
    # Use a version from MAM's list of approved clients instead of latest.
    image: lscr.io/linuxserver/qbittorrent:latest
    container_name: qbittorrent
    restart: unless-stopped
    environment:
      PUID: "1000"
      PGID: "1000"
      WEBUI_PORT: "8080"
    ports:
      - "8080:8080"
    volumes:
      - /srv/qbittorrent/config:/config
      - /srv/media/downloads:/media/downloads

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

If your qBittorrent service is already in the file, add only the `fetchshelf` service to it, and change
`qbittorrent` under `depends_on` to the name of your qBittorrent service if it differs.

### If qBittorrent runs some other way

If qBittorrent is installed directly on the machine, or runs in a compose file of its own, give Fetchshelf a
compose file of its own, in a folder such as `/srv/fetchshelf`:

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

Fetchshelf then reaches qBittorrent by the machine's address, the same one you use to open qBittorrent in your
browser (step 4).

### What the lines mean

| Line | What it does | What to change |
|---|---|---|
| `"3157:3157"` | The port of Fetchshelf's web page. | If port 3157 is taken on your machine, change the **first** number, for example `"3158:3157"`, and use that number in your browser. |
| `/srv/fetchshelf/config:/config` | The folder for `config.toml`, Fetchshelf's settings. | The folder you made in step 1. |
| `/srv/fetchshelf/database:/database` | The folder for Fetchshelf's database: what it knows about your torrents. | The folder you made in step 1. |
| `/srv/media:/media` | The shared folder with your downloads and library, seen inside Fetchshelf as `/media`. | Your shared folder from step 1. Keep it **one** line: two separate mounts for downloads and library stop hardlinks working, even on one disk. |
| `PUID` and `PGID` | The user Fetchshelf runs as, so the files it makes belong to you. | Run `id` on the machine. It prints something like `uid=1000(alex) gid=1000(alex)`: use those two numbers. On a NAS, use the numbers of the user that owns the shared folder, usually the same ones qBittorrent runs with. |

### Start it

In the folder with the compose file, run:

```sh
docker compose up -d
```

Then open `http://<your machine>:3157` in a browser, for example `http://192.168.1.10:3157`. Fetchshelf's web
page opens. About a minute after starting, `docker compose ps` shows Fetchshelf as `(healthy)`.

Nothing happens on MAM yet: Fetchshelf runs no jobs until it has your MAM session, which is the next step. You
can look around safely.

## Step 3: understand `config.toml`

On its first start, Fetchshelf created `config.toml` in its config folder (`/srv/fetchshelf/config` in the
example), holding a single line with an empty session id, `mam_id = ""`. All of Fetchshelf's settings go in this one file. Open it with
any text editor; on a NAS, its file manager or a text editor app will do.

The file is in a format called TOML. You only need to know four things about it:

- A setting is a name, `=` and a value: `min_ratio = 2`. Text goes in double quotes, `mam_id = "abc123"`;
  numbers don't.
- A line in double brackets, such as `[[qbittorrent]]`, starts a **block**: the settings under it, up to the
  next block, belong to it. Some blocks can appear more than once.
- Settings that don't belong to a block go at the **top of the file, before the first block.** A setting placed
  after a block belongs to that block: Fetchshelf either refuses it there, with a message such as
  `Property 'min_ratio' not found on type '...Library'`, or applies it to that block only.
- A line starting with `#` is a comment, for your own notes.

You add to the file in the next step, then check it and restart Fetchshelf once at the end.

## Step 4: your first configuration

### 1. Your MAM session

Fetchshelf signs in to MAM with a **session id**, which you create on MAM yourself:

1. On MAM, open your security preferences and create a new session. MAM locks a session to an address. If
   your home IP address changes from time to time, choose the option that allows that. If Fetchshelf's traffic
   goes through a VPN, the session must match the VPN's address, not your home's.
2. Copy the session id it gives you, a long string of letters and numbers.
3. Put it in `config.toml`, replacing the empty one:

   ```toml
   mam_id = "your-session-id"
   ```

Treat the session id like a password: anyone who has it can act as you on MAM. Don't post it anywhere,
including in a request for help.

To check that MAM accepts it, run this on the machine that runs Fetchshelf, with your session id in place of
`your-session-id`:

```sh
curl -b "mam_id=your-session-id" https://www.myanonamouse.net/json/checkCookie.php
```

If the answer contains `"Success":true`, the session works. Anything else means MAM didn't accept it: create a
new session and try again.

### 2. qBittorrent

Add the qBittorrent Web UI's address and sign-in at the end of the file:

```toml
[[qbittorrent]]
url = "http://qbittorrent:8080"
username = "admin"
password = "your-qbittorrent-password"
```

The `url` depends on how you installed Fetchshelf in step 2:

- **In the same compose file as qBittorrent:** `http://` and the qBittorrent service's name, then `:` and the
  Web UI port: `http://qbittorrent:8080`.
- **Any other way:** the machine's address, the one you use to open qBittorrent in your browser, such as
  `http://192.168.1.10:8080`.

Don't use `localhost` or `127.0.0.1`: inside Fetchshelf's container they mean the container itself, not your
machine.

If you set up qBittorrent's container just now, it has no password of its own yet: it prints a temporary one
in its log (`docker compose logs qbittorrent`), with the user name `admin`. Sign in with it, and set your own
password under **Options → Web UI**. Fetchshelf needs a password that stays the same.

**Only if you kept qBittorrent's own download folder** in step 1, also tell Fetchshelf how to translate
qBittorrent's paths. Add, right under the `[[qbittorrent]]` lines:

```toml
[qbittorrent.path_mapping]
"/downloads" = "/media/torrents"
```

It reads: where qBittorrent says `/downloads`, Fetchshelf uses `/media/torrents`. On the left goes the
**Default Save Path** as qBittorrent shows it; on the right the same folder as Fetchshelf sees it, under the
`/media` mount. In the step 1 example, with `/mnt/disk1` mounted as `/media`, `/mnt/disk1/torrents` is
`/media/torrents` to Fetchshelf. If qBittorrent and Fetchshelf both see the downloads at the same path, leave
this out.

### 3. Your library

A **library** tells Fetchshelf where finished books go. The simplest one takes every finished download in your
downloads folder:

```toml
[[library]]
name = "Books"
download_dir = "/media/downloads"
library_dir = "/media/library"
```

- `download_dir` is the downloads folder as Fetchshelf sees it. A finished torrent that qBittorrent saved there,
  or in a folder inside it, goes into this library.
- `library_dir` is where the books go. Fetchshelf makes a folder for each book in it.

Books end up like this:

```
/srv/media/library
├── Andy Weir
│   └── Project Hail Mary {Ray Porter}               an audiobook, with its narrator
└── Brandon Sanderson
    └── Stormlight Archive
        └── Stormlight Archive #1 - The Way of Kings   an ebook in a series
```

Only the book's own files are linked: the best audiobook format and the best ebook format in the torrent. Other
files, such as images or text files that came with it, are left out. Next to the files Fetchshelf writes a small
`metadata.json` with the book's details, which Audiobookshelf reads.

Torrents in that folder that didn't come from MAM, such as ones from another tracker, are looked up on MAM, not
found, and listed on the **Errors** page as *MAM has no torrent with this info-hash*. That does no harm; you can
remove the entries there.

#### Optional: audiobooks and ebooks apart

Many people keep audiobooks and ebooks in separate libraries, for example because Audiobookshelf reads one and
an e-reader app the other. Instead of the `[[library]]` above, add:

```toml
[[tag]]
categories = { audio = true, ebook = false }
category = "audiobooks"

[[tag]]
categories = { audio = false, ebook = true }
category = "ebooks"

[[library]]
name = "Audiobooks"
category = "audiobooks"
library_dir = "/media/library/audiobooks"

[[library]]
name = "Ebooks"
category = "ebooks"
library_dir = "/media/library/ebooks"
```

The two `[[tag]]` blocks give every audiobook Fetchshelf downloads the qBittorrent category `audiobooks`, and
every ebook the category `ebooks`. Each `[[library]]` takes the finished torrents of its category. Fetchshelf
creates the categories in qBittorrent itself. A torrent you add in qBittorrent by hand goes into a library if
you give it one of these categories.

### 4. Keep your account safe

Add these three lines near the **top** of the file, under `mam_id` and before the first block:

```toml
min_ratio = 2
unsat_buffer = 10
wedge_buffer = 5
```

| Setting | What Fetchshelf will never do |
|---|---|
| `min_ratio` | Start a download that would take your ratio below this. Free downloads cost no ratio. |
| `unsat_buffer` | Use your last unsatisfied-snatch slots: it always leaves this many free. |
| `wedge_buffer` | Use a freeleech wedge that would leave you fewer than this. |

Without these lines, Fetchshelf uses `2`, `10` and `0`. Start strict; you can loosen them later. A download that
doesn't fit waits on the **Selected** page and is tried again on the next run.

### 5. Download your free bookmarks

Add an **autograb**, a search that Fetchshelf runs every half hour:

```toml
[[autograb]]
name = "Free bookmarks"
type = "bookmarks"
cost = "free"
```

It reads your bookmarks on MAM and downloads every one that is free: freeleech or VIP. With `cost = "free"`
nothing it downloads costs ratio or wedges. The name is what the job is called on the **Jobs** page.

Be aware that it takes **all** your free bookmarks, as many as your unsatisfied-snatch slots allow. If you have
many bookmarks, see what it would download first with a **dry run** (in [Step 6](#step-6-see-it-work)).

While you try things out, you can also add `add_torrents_stopped = true` at the top of the file. qBittorrent
then adds every torrent stopped, and nothing downloads until you start it there. Remove the line when you are
happy.

### The whole file

With everything above, and the simple single library, `config.toml` looks like this:

```toml
# Your MAM session id. Treat it like a password.
mam_id = "your-session-id"

# Never start a download that would take the account below this ratio.
min_ratio = 2
# Always leave this many unsatisfied-snatch slots free.
unsat_buffer = 10
# Never use a wedge that would leave fewer than this many.
wedge_buffer = 5

[[qbittorrent]]
url = "http://qbittorrent:8080"
username = "admin"
password = "your-qbittorrent-password"

[[autograb]]
name = "Free bookmarks"
type = "bookmarks"
cost = "free"

[[library]]
name = "Books"
download_dir = "/media/downloads"
library_dir = "/media/library"
```

## Step 5: check and restart

Before restarting, let Fetchshelf check the file. In the folder with the compose file, run:

```sh
docker compose exec fetchshelf dotnet /app/Fetchshelf.Web.dll --check-config
```

When the file is fine, it starts with `Configuration OK` and lists what Fetchshelf found:

```
Configuration OK: /config/config.toml

MAM: mam_id set; background jobs run
Safety: min_ratio 2, unsat_buffer 10, wedge_buffer 5
Allowed hosts: any (allowed_hosts not set)
qBittorrent:
  http://qbittorrent:8080
Libraries:
  Books  download_dir /media/downloads  -> /media/library  hardlink_or_copy, metadata.json
Jobs:
  Link            every 10 min
  Clean           after every Link run
  Free bookmarks  autograb bookmarks, cost free, every 30 min
  Download        every 30 min, and after selections
Tag rules: 0
```

When something is wrong, it says what, for example:

```
Fetchshelf would not start with /config/config.toml: Invalid configuration: unrecognized autograb type 'bookmark'.
```

Fix what it names and check again. Fetchshelf is strict on purpose: a misspelled setting would otherwise do
nothing without a word. The check changes nothing, so run it as often as you like.

When it says `Configuration OK`, restart Fetchshelf:

```sh
docker compose restart fetchshelf
```

Fetchshelf reads `config.toml` only when it starts, so after every change to the file: check, then restart.
Restarting is safe at any time; nothing is lost.

## Step 6: see it work

Open Fetchshelf in your browser again. The bar at the top has the pages you need now:

- **Jobs** lists what Fetchshelf does in the background, each with when it last ran, how it went and when it
  runs next. **Run now** runs a job straight away. The ones you have now:
  - **Free bookmarks**, your autograb: reads your bookmarks and picks the free ones.
  - **Download**: adds what was picked to qBittorrent, within your limits. It runs right after an autograb picks
    something.
  - **Link**: puts finished downloads into your library, every 10 minutes. A download Fetchshelf added is
    linked within about a minute of finishing.
  - **Clean**: runs after Link and tidies up when a better copy of a book replaces a worse one.
- **Selected** is the queue: what was picked and is waiting to be downloaded. Something stays here while it
  doesn't fit your limits.
- **Torrents** lists every torrent Fetchshelf knows, and where each is in your library. **Check qBittorrent**
  there asks qBittorrent about all of them, a quick test that the connection works.
- **Errors** lists what went wrong, torrent by torrent, and jobs that keep failing. A number on its link means
  something needs a look.
- **Search** (the home page) searches MAM. **Download** on a result downloads it like an autograb would, within
  your limits; **Wedge download** spends a freeleech wedge on it, so it costs no ratio.
- **Account** shows your MAM numbers and how much Fetchshelf may still download.

### A dry run first

To see what your autograb would download, without downloading anything: on the **Jobs** page, press **Dry run**
next to **Free bookmarks**. When it has finished, click its name to open its history, and unfold the run: it
lists each torrent it would pick, and each one it turned down and why. A dry run selects, records and downloads
nothing.

### Your first download

1. On MAM, bookmark a book that is freeleech.
2. On the **Jobs** page, press **Run now** next to **Free bookmarks**.
3. When it has run, its result says how many torrents it selected, and the book shows on the **Selected** page,
   then in qBittorrent.
4. When qBittorrent has finished it, the book appears in your library folder within a minute or so, and on the
   **Torrents** page.

From now on this happens by itself: bookmark a free book on MAM, and within half an hour Fetchshelf has it
downloading. Once qBittorrent has finished it, it is in your library.

## Keeping it running

### Updating

New versions of Fetchshelf come out regularly. Before updating, read the release notes in
[RELEASES.md](../RELEASES.md), especially the *Upgrading* part of every version since yours, and take a backup
(below): a new version can upgrade the database, and going back to an older version then needs the backup.

To update, in the folder with the compose file:

```sh
docker compose pull fetchshelf
docker compose up -d fetchshelf
```

Your settings and database stay in their folders. The version you run is shown at the bottom of every page.

If you'd rather choose when to update, use a version number instead of `latest` in the compose file, such as
`ghcr.io/crimsonking60/fetchshelf:2026.11.18`, and change it when you decide to update.

### Backups

Fetchshelf's own data is two small folders: the config folder (your `config.toml`) and the database folder. To
back them up, stop Fetchshelf, copy both folders, and start it again:

```sh
docker compose stop fetchshelf
cp -a /srv/fetchshelf /srv/fetchshelf-backup
docker compose start fetchshelf
```

Stop it first: a copy of the database taken while Fetchshelf writes to it can be unusable. Your library is the
downloaded files, linked; back those up the way you back up the rest of your media.

## If something doesn't work

Look first at the **Errors** page and at Fetchshelf's log:

```sh
docker compose logs --tail 100 fetchshelf
```

**Nothing runs; the Jobs page shows no runs.** `mam_id` is empty, or Fetchshelf wasn't restarted after you set
it. Set it, check and restart.

**Every job fails with *MAM refused the request (HTTP 403)*.** MAM didn't accept the session: it has expired, or
it is locked to another address than the one Fetchshelf's traffic comes from (a VPN, or a changed home IP).
Fetchshelf then pauses its MAM requests for 30 minutes. Create a new session on MAM, put it in `mam_id`, and
restart, which also ends the pause. The `curl` check from [step 4](#1-your-mam-session) tells you whether MAM
accepts a session id.

**qBittorrent can't be reached.** Check the `url`, user name and password, and that qBittorrent's Web UI is on.
Use the service name or the machine's address, not `localhost`. **Check qBittorrent** on the **Torrents** page
tests the connection after a restart.

**Downloads finish, but nothing appears in the library.** The **Errors** page says why for each torrent:

- *Source missing*: Fetchshelf can't see the folder qBittorrent reported. qBittorrent's **Default Save Path**
  isn't under the shared folder, or the path needs translating with `path_mapping` (step 1 and step 4).
- *No library matched*: no `[[library]]` takes the torrent. With `download_dir`, check that it is the folder
  qBittorrent saves to, as Fetchshelf sees it. With categories, check that the torrent has one of them in
  qBittorrent.
- *No author*: MAM has no author for this book. **Edit**, on its row on the **Errors** or **Torrents** page,
  adds one, and the next Link run links it.

**The library takes as much space as the downloads.** The files were copied, not hardlinked: the downloads and
the library are on different disks, or reach Fetchshelf through two mounts instead of one. Mount the folder
that holds both, as in step 2, and keep both on one disk.

**Permission errors in the log or on the Errors page** (*permission denied*, `EACCES`). `PUID` and `PGID` aren't
the owner of the config, database or media folders. Set them to the numbers from `id` for the user that owns the
folders, then run `docker compose up -d`.

**The container keeps restarting.** Usually the configuration: the log names the setting at fault. While it
restarts, the check from step 5 can't reach it, so check the file with a container of its own instead:

```sh
docker run --rm --entrypoint dotnet -v /srv/fetchshelf/config:/config:ro ghcr.io/crimsonking60/fetchshelf /app/Fetchshelf.Web.dll --check-config
```

**Nothing is downloaded.** Your bookmarks have nothing free at the moment, or the limits hold it: what was picked
then waits on the **Selected** page, and the **Account** page shows what is left of your slots and ratio. A book
you already have in the library, in an equal or better format, is skipped.

### Getting help

- **Questions:** ask in [Discussions](https://github.com/crimsonking60/fetchshelf/discussions), under **Q&A**. Or ask in the
  Fetchshelf thread in the MAM forums.
- **Bugs:** [open an issue](https://github.com/crimsonking60/fetchshelf/issues/new/choose) with the **Bug
  report** form.
- **Include** the Fetchshelf version (at the bottom of every page), what you expected and what happened, the
  lines from the log, and your `config.toml` **with `mam_id` and passwords replaced** by placeholders.

## What else Fetchshelf can do

What this page set up is the core. When you're ready for more, the [user guide](user-guide.md) covers
everything, and every setting is in the [configuration reference](configuration.md). A taste:

- **Search for more than your bookmarks.** Autograbs can look for new uploads, authors or series, filter by
  language, format, size and more, and pay with ratio or wedges within your limits. See
  [Automatic downloads](user-guide.md#automatic-downloads).
- **Books you want, from elsewhere.** Your *Want to Read* shelf and lists on [Hardcover](https://hardcover.app),
  a plain file of titles, or your bookmarks as a list, each searched on MAM book by book. See
  [Books you want, from Hardcover](user-guide.md#books-you-want-from-hardcover),
  [Books you want, from a file](user-guide.md#books-you-want-from-a-file) and
  [Your MAM bookmarks as a list](user-guide.md#your-mam-bookmarks-as-a-list).
- **Complete series and follow authors.** Fetchshelf can watch a series or an author for books you don't have
  yet, and download them or list them for you to choose. See
  [Completing series and following authors](user-guide.md#completing-series-and-following-authors).
- **Better copies.** A better format of a book you have replaces the worse one, and Fetchshelf can go looking for
  better copies. See [Better formats replace worse ones](user-guide.md#better-formats-replace-worse-ones).
- **Look after your account.** See which torrents still need seeding before MAM counts them as satisfied, and
  spend bonus points before they reach MAM's cap. See [Protecting your account](user-guide.md#protecting-your-account).
- **Your library, your way.** Keep [Audiobookshelf](user-guide.md#connecting-to-audiobookshelf) up to date, read
  on an e-reader through an [OPDS catalog](user-guide.md#reading-on-an-e-reader-opds), keep
  [comics and manga for Komga](user-guide.md#comics-and-manga-for-komga), split
  [collections](user-guide.md#collections) into their books, and choose
  [folder names](user-guide.md#folder-names) and [link methods](user-guide.md#link-methods).
- **Keep an eye on things.** [Notifications](user-guide.md#getting-notified), for example on your phone, when
  something is grabbed or a job keeps failing, your [account's numbers over time](user-guide.md#account), a
  [check of the library](user-guide.md#checking-the-library) for stray folders and missing files, and
  [backups](user-guide.md#backups-while-fetchshelf-runs) while Fetchshelf runs.
