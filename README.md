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
> Development happens on `main`, so the documentation on `main` may describe changes that are not released yet.
> If you run a released version, use the [documentation site](https://fetchshelf.readthedocs.io/en/stable/), which
> has a version for every release from 2026.11.9 on.

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

Fetchshelf runs in Docker. New to it? [Getting started](https://fetchshelf.readthedocs.io/en/stable/getting-started/)
takes you through installing it with Docker Compose, connecting it to MAM and qBittorrent, and a first library and
automatic downloads, step by step. The [user guide](https://fetchshelf.readthedocs.io/en/stable/user-guide/) covers
everything else, including Audiobookshelf, and every setting is in the
[configuration reference](https://fetchshelf.readthedocs.io/en/stable/configuration/).

## Status

This project is under active, incremental development.

## Untested

These features are built and covered by automated tests, but have not yet been confirmed working on a real
setup. If you use one, please report how it went, good or bad (see
[Getting help](https://fetchshelf.readthedocs.io/en/stable/user-guide/#getting-help)).

- The `linux/arm/v7` image. The `linux/amd64` and `linux/arm64` images are confirmed.
- `mark_removed` in `[[autograb]]`.
- VIP top-ups from `[bonus]` (`vip = true`): MAM's answer to them has not been seen yet. Wedges and upload credit
  are confirmed.
- `on_invalid_torrent` in `[[qbittorrent]]`.
- `[[hardcover_list]]`: matching by ISBN or ASIN, the weekly search for books MAM doesn't have, and searching again
  for a selection that was removed. Another user's list is confirmed.
- `[follow] audiobookshelf_series` leaving the books Audiobookshelf has out of the Follow job's finds. Following the
  series and stopping one are confirmed.
- `comic_types`, globally and in a `[[library]]`.
- `[[notify]]`: the Gotify, Discord and Apprise formats follow those services' documented APIs but have not been
  tried against the services themselves. The `ntfy` format is confirmed, and the `json` format and the messages'
  contents are covered by tests.

## Latest release

**2026.11.25** (2026-11-27). See [RELEASES.md](RELEASES.md#20261125---2026-11-27) for what changed, and for
every earlier release. Its documentation is on the [documentation site](https://fetchshelf.readthedocs.io/en/stable/).

## Contributing

Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), and see the
[contributors guide](docs/contributors-guide.md) for setting up a development machine and the full workflow.

## License

MIT — see [LICENSE](LICENSE). Fetchshelf also contains other software under its own permissive licenses, listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); keep that file in a fork.

## Oh, and, by the way:

Trans rights are still human rights.
