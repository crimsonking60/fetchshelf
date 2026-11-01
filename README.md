<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/logo-dark.svg">
    <img src="docs/images/logo-light.svg" alt="" height="32" align="bottom">
  </picture>
  Fetchshelf
</h1>

Fetchshelf is a self-hosted service that connects MAM with a BitTorrent client (qBittorrent). It
downloads and organizes anything MAM carries — ebooks, audiobooks, comics and manga, periodicals,
musicology and radio — and can optionally keep [Audiobookshelf](https://www.audiobookshelf.org) up to
date as well. It is a C# reimplementation of MLM ("MyAnonamouse Library Manager"), an abandoned Rust
application.

Fetchshelf has two independent, optional halves:

1. **Auto-downloader** — finds torrents on MAM matching user-defined rules and adds them to
   qBittorrent, while protecting the account's ratio, unsatisfied-snatch slots, and freeleech-wedge
   supply.
2. **Library organizer** — watches qBittorrent for finished torrents and links the wanted files into
   a clean `Author/Series/Title` folder tree, with a `metadata.json` file next to every title holding
   its authors, series, description, ISBN, genres and more. The tree and the metadata files work
   with any reader, player, file manager or script; Audiobookshelf reads them directly.

## Getting started

Fetchshelf runs in Docker. The [user guide](docs/user-guide.md) walks you through installing it with
Docker or Docker Compose, connecting it to MAM and qBittorrent (and, if you use it, Audiobookshelf),
and setting up your library and automatic downloads. For every setting, see the
[configuration reference](docs/configuration.md).

## Status

This project is under active, incremental development.

## Untested

These features are built and covered by automated tests, but have not yet been confirmed working on a real
setup. If you use one, please report how it went, good or bad (see
[Getting help](docs/user-guide.md#getting-help)).

- Scheduled searches: [`[[autograb]]`](docs/configuration.md#autograb) blocks. Downloads started from the
  Search page are confirmed.
- [Notion lists](docs/configuration.md#notion_list).
- [Snatchlists](docs/configuration.md#snatchlist).
- More than one [`[[qbittorrent]]`](docs/configuration.md#qbittorrent) entry, and `path_mapping`.
- The [link methods](docs/configuration.md#library) other than `hardlink`: `copy`, `symlink`,
  `hardlink_or_copy`, `hardlink_or_symlink` and `no_link`.
- [Better formats replacing worse ones](docs/user-guide.md#better-formats-replace-worse-ones), including
  `on_cleaned`.
- [Refresh and relink](docs/user-guide.md#when-the-folder-no-longer-matches) after a metadata or
  configuration change.
- The [Audiobookshelf integration](docs/configuration.md#audiobookshelf).
- Reading the session id from a file with [`mam_id_file`](docs/configuration.md#reading-mam_id-from-a-file).
- The `linux/arm/v7` image. The `linux/amd64` and `linux/arm64` images are confirmed.

## Latest release

**2026.10.3** (2026-10-31). See [RELEASES.md](RELEASES.md#2026103---2026-10-31) for what changed, and for
every earlier release.

## Contributing

Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), and see the
[contributors guide](docs/contributors-guide.md) for setting up a development machine and the full workflow.

## License

MIT — see [LICENSE](LICENSE).
