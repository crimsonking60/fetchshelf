# A getting-started guide for new users — plan

## Context

`docs/user-guide.md` has grown to some 2,600 lines and covers everything: thirteen kinds of configuration section,
the Following and Upgrade jobs, OPDS, collections, seeding obligations, the Library check. It is the right reference,
but someone setting Fetchshelf up for the first time has to pick the few things they need out of all of it. Its own
introduction assumes Docker, a text editor and MAM's terms, and its first configuration step sends the reader to five
example files and the configuration reference.

A new page, `docs/getting-started.md`, takes a new user from nothing to a working setup with the features of the
first releases: free bookmarks downloaded automatically, finished downloads linked into a tidy library, the safety
limits, and the web interface's main pages. It ends with a short tour of what else Fetchshelf can do, pointing to the
user guide for each. The user guide and the configuration reference stay as they are.

## Reader

- A MAM user, who knows ratio, unsatisfied snatches, freeleech, wedges and VIP. These terms aren't explained (as
  in the user guide).
- Has a PC or NAS running qBittorrent, quite possibly in Docker, and may have seen a `docker-compose.yaml` before.
- Not a developer. Can follow numbered steps, copy a file and edit it, run a command they are given. May not know
  what a mount, a hardlink, a container's own `localhost` or a user id is, and shouldn't need to before it matters.

## Scope

**In:** installing with Docker Compose; the folder layout that makes hardlinks work; a first `config.toml` built up
section by section (`mam_id`, one `[[qbittorrent]]`, the safety limits, one `[[autograb]]` of free bookmarks, one or
two `[[library]]`); checking the config; seeing it work in the web interface; the Search page for downloading by hand;
updating and backing up; the four or five problems most first setups run into.

**Out** (only named in the closing tour): Audiobookshelf, `[[tag]]` rules beyond the audiobook/ebook split, other
autograb types and filters, paid costs, Hardcover and file lists, the bookmarks list, Following, Upgrade, Bonus,
seeding obligations, notifications, OPDS, the series layout and Komga, collections, metadata files and covers, the
Library check, Duplicates, Statistics and Account, scheduled backups, `allowed_hosts`, `fs`, `docker run` and the
other environment variables.

## Writing rules

- Plain words and short steps; one idea per paragraph. A technical term is explained the first time it is needed,
  in a sentence, and only as far as this guide needs it (for example a hardlink: "the same file in two places,
  taking the space once").
- Every step says what to expect when it works ("the **Jobs** page lists ..."), so the reader knows they can go on.
- Each section ends where the user guide takes over, with a link to the matching section for more.
- Every example is complete and works when copied with the reader's own values: one `docker-compose.yaml`, and one
  `config.toml` that grows through the guide and is shown whole at the end.
- The docs decisions hold: no screenshots, no glossary of MAM terms, no links into MAM's own pages (refer to "MAM's
  own documentation" or "your security preferences on MAM" in words), no to-do lists.
- The reader is addressed as in the user guide ("you"). The guide doesn't mention its writer.
- Nothing in it contradicts the user guide or the configuration reference. Wherever both describe the same thing, the
  wording follows the user guide's, simplified rather than changed.

## Outline of `docs/getting-started.md`

1. **What Fetchshelf does.** Three or four sentences: it watches your MAM bookmarks and downloads the free ones with
   qBittorrent, keeps your ratio, unsatisfied snatches and wedges safe while doing it, and puts finished books into a
   clean `Author/Series/Title` library that apps like Audiobookshelf can read. What this page covers and how long it
   takes, roughly. A note that everything here can be changed later without starting over.

2. **What you need.** A MAM account in good standing; qBittorrent with its Web UI on, an approved version (check
   MAM's list); Docker with Compose (`docker compose version` shows whether it is there); a few gigabytes free. A
   sentence on NAS boxes: most run Docker through their own app (Container Manager, App Center) and accept a
   compose file. This page doesn't give steps for each brand.

3. **Plan your folders.** The one part to get right before installing. A picture of one shared folder holding
   both the downloads and the library:

   ```
   /srv/media
   ├── downloads     qBittorrent saves here
   └── library
       ├── audiobooks
       └── ebooks
   ```

   - Why: Fetchshelf *links* each finished book into the library: the same file in two places, using the space once,
     and qBittorrent keeps seeding. That only works when both folders are on the same disk and reach Fetchshelf
     through one mount.
   - The rule: qBittorrent and Fetchshelf both mount the shared folder **at the same path** (`/srv/media:/media` in
     both). Then the paths qBittorrent reports are paths Fetchshelf can open.
   - If qBittorrent already saves somewhere else: either move its download folder under the shared folder, or keep
     it and read [Paths: `path_mapping`](user-guide.md#paths-path_mapping). One sentence on what happens otherwise:
     files are copied (as much space again), or not found.

4. **Install.** Two cases, each a complete `docker-compose.yaml`:
   - **qBittorrent already runs in Docker Compose:** add the `fetchshelf` service to the same file, next to it
     (the user guide's example in "Together with qBittorrent and Audiobookshelf", without Audiobookshelf). It can then
     reach qBittorrent by its service name.
   - **qBittorrent runs elsewhere** (on the host, on another machine, in another compose file): Fetchshelf's own file,
     reaching qBittorrent by the machine's address.

   Then each line explained in a short table: the port, the `config` and `database` folders, the shared media
   folder, and `PUID`/`PGID` (run `id`, use the numbers of the user that owns the media folder, usually the same
   ones qBittorrent runs as). Start it with `docker compose up -d`, open `http://<your machine>:3157`. Expected: the
   web interface opens, and nothing happens on MAM yet, by design.

5. **Your first `config.toml`.** Fetchshelf created `config.toml` in the config folder with only an empty `mam_id`.
   Built up in steps, each a short block to add, with what each line means:

   1. **Your MAM session.** Create a session in your security preferences on MAM, locked the way that suits where
      Fetchshelf runs (an IP address that changes needs the more tolerant option). Paste it as `mam_id`. Treat it like
      a password. The optional `curl` check from the user guide, with what a good answer looks like.
   2. **qBittorrent.** `[[qbittorrent]]` with `url`, `username`, `password`. Which `url` to use for each install
      case from step 4, and the most common mistake: `localhost` inside a container means the container itself, not
      the machine.
   3. **Your library.** The simplest start: one `[[library]]` with `download_dir` and `library_dir`, so every finished
      download under the downloads folder is linked, with no qBittorrent categories needed (as in example 1). Then,
      optionally, audiobooks and ebooks apart: two `[[tag]]` rules that give each its qBittorrent category and two
      `[[library]]` blocks that pick them up by category. What the library looks like afterwards, one example path
      for an ebook and one for an audiobook.
   4. **Keep your account safe.** `min_ratio`, `unsat_buffer` and `wedge_buffer`, with their defaults, in one sentence
      each: what Fetchshelf will never do. A suggestion to start strict.
   5. **Download your free bookmarks.** One `[[autograb]]` with `type = "bookmarks"` and `cost = "free"`: every
      bookmark that is freeleech or VIP is downloaded, nothing that costs ratio. That it runs every 30 minutes
      (`search_interval`), and that new links follow every 10 (`link_interval`). Optionally, while trying things
      out, `add_torrents_stopped = true` and the **Dry run** button.
   6. **Check and restart.** `docker compose exec fetchshelf dotnet /app/Fetchshelf.Web.dll --check-config` and
      what its two possible answers look like, then `docker compose restart fetchshelf`. Fetchshelf won't start
      with a typo in the file, and the log says which line. That's the safety net, not a failure.

   The whole file once more, complete.

6. **See it work.** A short walk through the pages that matter now:
   - **Jobs**: each job, when it last ran and how it went; **Run now**.
   - **Selected**: what is queued for download and why it might wait (the safety limits).
   - **Torrents**: what Fetchshelf knows about, and whether each is linked.
   - **Errors**: anything that went wrong, per torrent.
   - **Search**: search MAM by hand and download one with a click, at the cost the page shows.

   A first test: bookmark a free book on MAM, press **Run now** for the autograb on the **Jobs** page, watch it
   reach qBittorrent and, once it finishes, the library folder.

7. **Keeping it running.**
   - Updating: `docker compose pull` then `docker compose up -d`, and reading the release notes' *Upgrading* part
     first. Pinning a version instead of `latest` for those who prefer to choose when.
   - Backups: stop the container and copy the config and database folders. The library itself is the books in
     qBittorrent's folder, linked. Pointer to scheduled backups in the user guide.
   - Changing a setting: edit, check, restart; restarting is safe at any time.

8. **If something doesn't work.** Each a symptom, the usual cause and the fix, with a link to the user guide's
   Troubleshooting entry:
   - Nothing runs: `mam_id` is empty, or MAM refused it (session locked to another address, VPN).
   - qBittorrent can't be reached: the `url` (container name, `localhost`), the Web UI isn't on, or the password.
   - Downloads finish but nothing appears in the library, or the files are copied: the folder layout from step 3,
     the two mounts not the same.
   - Permission errors in the log: `PUID`/`PGID` don't own the folders.
   - Nothing is downloaded: no free bookmarks, or the safety limits hold it (the **Selected** page says which).

   And where to ask: [Getting help](user-guide.md#getting-help).

9. **What else Fetchshelf can do.** The teaser. A short paragraph per group, each naming the features in plain words
   and linking to the user guide's section, so a reader can tell whether a feature is for them without reading it:
   - **Find more than your bookmarks:** autograb searches for new uploads, authors and series, with filters for
     language, format, size and more, and costs that pay with ratio or wedges within the limits
     ([Automatic downloads](user-guide.md#automatic-downloads)).
   - **Books you want, from elsewhere:** a Hardcover *Want to Read* shelf or list, or a plain file of titles; your
     MAM bookmarks as a list ([Hardcover](user-guide.md#books-you-want-from-hardcover),
     [a file](user-guide.md#books-you-want-from-a-file), [bookmarks](user-guide.md#your-mam-bookmarks-as-a-list)).
   - **Complete series and follow authors,** and **upgrade to better copies** of what you have
     ([Completing series](user-guide.md#completing-series-and-following-authors),
     [Better formats](user-guide.md#better-formats-replace-worse-ones)).
   - **Look after the account:** seeding until MAM counts a torrent as satisfied, spending bonus points
     ([Protecting your account](user-guide.md#protecting-your-account)).
   - **Your library, your way:** Audiobookshelf kept up to date, an OPDS catalog for e-readers, comics and manga for
     Komga, collections split into their books, folder names and metadata files
     ([Audiobookshelf](user-guide.md#connecting-to-audiobookshelf), [OPDS](user-guide.md#reading-on-an-e-reader-opds),
     [Comics and manga](user-guide.md#comics-and-manga-for-komga), [Collections](user-guide.md#collections)).
   - **Keep an eye on things:** notifications, the account's numbers over time, the Library check, duplicates
     ([Getting notified](user-guide.md#getting-notified), [Using the web interface](user-guide.md#using-the-web-interface)).

   Closing line: the [user guide](user-guide.md) covers all of it, and every setting is in the
   [configuration reference](configuration.md).

## Changes around it

- **`docs/index.md`:** `getting-started` first in the *Using Fetchshelf* table of contents, and the paragraph that
  says "Start with the user guide" changed to: new to Fetchshelf, start with *Getting started*; the user guide is
  the full reference.
- **`docs/user-guide.md`:** one sentence in its introduction pointing a first-time reader to *Getting started*.
  Nothing else in it changes.
- **`README.md`:** the installation paragraph points to *Getting started* first, then to the user guide.
- **The same "development happens on `main`" note** at the top of the new page as on the user guide, between the
  `github-only` markers, so GitHub readers are pointed to the documentation of the version they run.

## Checking it

- **Every step tried on a clean machine, as written:** a new folder, the compose file copied, the config built up
  step by step, no knowledge beyond the page. A throwaway instance (its own port, a test `mam_id` placeholder) for
  everything up to MAM, without contacting MAM. The MAM steps are checked against the user guide's description, not
  run.
- **Every command and setting checked against the code:** `--check-config` and its output, the defaults
  (`search_interval`, `link_interval`, `min_ratio`, `unsat_buffer`, `wedge_buffer`), the compose service, the
  paths in the example library folder. The final `config.toml` passes `--check-config`.
- **Every link resolves:** the docs build without warnings (`sphinx-build -W` as for the user guide; the anchors
  come from `myst_heading_anchors`), and the GitHub view of the file has working relative links.
- **Read by the user** for tone and level before merging: it is for readers unlike its writer, so a read by
  someone setting it up from scratch is the real test, if one can be found.

## Steps

One pull request: the new page, the three pointers to it, and nothing else in the docs. Docs only, no code.
