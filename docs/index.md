# Fetchshelf

Fetchshelf is a self-hosted service that connects MAM with a BitTorrent client (qBittorrent). It downloads and
organizes anything MAM carries — ebooks, audiobooks, comics and manga, periodicals, musicology and radio — and
can optionally keep [Audiobookshelf](https://www.audiobookshelf.org) up to date as well.

It has two independent, optional halves:

1. **Auto-downloader** — finds torrents on MAM matching user-defined rules, or the books on a
   [Hardcover](https://hardcover.app) *Want to Read* shelf and lists, and adds them to qBittorrent, while
   protecting the account's ratio, unsatisfied-snatch slots, and freeleech-wedge supply.
2. **Library organizer** — watches qBittorrent for finished torrents and links the wanted files into a clean
   `Author/Series/Title` folder tree, with a `metadata.json` file next to every title, and optionally a
   Calibre-style `metadata.opf`. With `[opds]`, it also serves the ebook libraries as an OPDS catalog.

Start with the [user guide](user-guide.md), which walks through installing Fetchshelf with Docker and setting it
up. Every setting is in the [configuration reference](configuration.md).

These pages exist for every release: choose the version you run in the version menu. *latest* follows
development and may describe changes that are not released yet; *stable* is the newest release.

The source code, issues and release notes are on [GitHub](https://github.com/crimsonking60/fetchshelf).

```{toctree}
:caption: Using Fetchshelf
:maxdepth: 1

user-guide
configuration
config-examples/README
database-setup
fs
```

```{toctree}
:caption: Contributing
:maxdepth: 1

contributors-guide
maintainer-guide
```
