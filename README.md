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

> [!NOTE]
> The documentation follows the branch you are viewing: **`main`** matches the latest release, **`develop`**
> has changes that are not released yet. If you run a released version, use the
> [user guide](https://github.com/crimsonking60/fetchshelf/blob/main/docs/user-guide.md) and the
> [configuration reference](https://github.com/crimsonking60/fetchshelf/blob/main/docs/configuration.md) from `main`.

Fetchshelf has two independent, optional halves:

1. **Auto-downloader** — finds torrents on MAM matching user-defined rules, or the books on your
   [Hardcover](https://hardcover.app) *Want to Read* shelf and lists, and adds them to qBittorrent, while
   protecting the account's ratio, unsatisfied-snatch slots, and freeleech-wedge supply.
2. **Library organizer** — watches qBittorrent for finished torrents and links the wanted files into
   a clean `Author/Series/Title` folder tree, with a `metadata.json` file next to every title holding
   its authors, series, description, ISBN, genres and more, and optionally a Calibre-style `metadata.opf`.
   With `[opds]`, it also serves the ebook libraries as an OPDS catalog for e-readers and reading apps.
   The tree and the metadata files work with any reader, player, file manager or script; Audiobookshelf
   reads them directly.

## Getting started

Fetchshelf runs in Docker. The [user guide](https://github.com/crimsonking60/fetchshelf/blob/main/docs/user-guide.md) walks you through installing it with
Docker or Docker Compose, connecting it to MAM and qBittorrent (and, if you use it, Audiobookshelf),
and setting up your library and automatic downloads. For every setting, see the
[configuration reference](https://github.com/crimsonking60/fetchshelf/blob/main/docs/configuration.md).

## Status

This project is under active, incremental development.

## Untested

These features are built and covered by automated tests, but have not yet been confirmed working on a real
setup. If you use one, please report how it went, good or bad (see
[Getting help](https://github.com/crimsonking60/fetchshelf/blob/main/docs/user-guide.md#getting-help)).

- The `linux/arm/v7` image. The `linux/amd64` and `linux/arm64` images are confirmed.
- `path_mapping` in `[audiobookshelf]`.
- `mark_removed` in `[[autograb]]`.
- `on_invalid_torrent` in `[[qbittorrent]]`.

## Latest release

**2026.11.7** (2026-11-10). See [RELEASES.md](RELEASES.md#2026117---2026-11-10) for what changed, and for
every earlier release.

## Contributing

Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), and see the
[contributors guide](docs/contributors-guide.md) for setting up a development machine and the full workflow.

## License

MIT — see [LICENSE](LICENSE).

## Oh, and, by the way:

Trans rights are still human rights.
