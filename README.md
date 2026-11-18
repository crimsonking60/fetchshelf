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

Fetchshelf runs in Docker. The [user guide](https://fetchshelf.readthedocs.io/en/stable/user-guide/) walks you through installing it with
Docker or Docker Compose, connecting it to MAM and qBittorrent (and, if you use it, Audiobookshelf),
and setting up your library and automatic downloads. For every setting, see the
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
  for a selection that was removed.
- `unsat_buffer`, `wedge_buffer` and `max_active_downloads` in `[[hardcover_list]]`, `[follow]` and `[upgrade]`, and
  `cost = "wedge"` downloads starting while the ratio budget is spent.
- `hardlink_or_copy` and `hardlink_or_symlink` falling back only when a hardlink isn't possible there, and the tag-rule
  preview on the Config page.
- Folder names shortened to 255 bytes. `folder_names` and `folder_template` are confirmed.
- `allowed_hosts`, and the `[opds]` login.
- `[seeding]`: the Seeding job's estimate, `tag_unsatisfied`, the held-back `on_cleaned` and the
  `unsatisfied_not_seeding` notification.
- **Dry run** on the Jobs page, and the lines under each run in a job's history.
- `layout = "series"` read by Komga: the linking is confirmed, Komga's view of it is not.
- `[follow] audiobookshelf_series` leaving the books Audiobookshelf has out of the Follow job's finds. Following the
  series and stopping one are confirmed.
- `[[notify]]`: the Gotify, Discord and Apprise formats follow those services' documented APIs but have not been
  tried against the services themselves. The `ntfy` format is confirmed, and the `json` format and the messages'
  contents are covered by tests.

## Latest release

**2026.11.14** (2026-11-18). See [RELEASES.md](RELEASES.md#20261114---2026-11-18) for what changed, and for
every earlier release. Its documentation is on the [documentation site](https://fetchshelf.readthedocs.io/en/stable/).

## Contributing

Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), and see the
[contributors guide](docs/contributors-guide.md) for setting up a development machine and the full workflow.

## License

MIT — see [LICENSE](LICENSE).

## Oh, and, by the way:

Trans rights are still human rights.
