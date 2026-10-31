<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/logo-dark.svg">
    <img src="docs/images/logo-light.svg" alt="" height="32" align="bottom">
  </picture>
  Fetchshelf
</h1>

Fetchshelf is a self-hosted service that connects MAM with a BitTorrent client (qBittorrent) and,
optionally, [Audiobookshelf](https://www.audiobookshelf.org). It is a C# reimplementation of
MLM ("MyAnonamouse Library Manager"), an abandoned Rust
application.

Fetchshelf has two independent, optional halves:

1. **Auto-downloader** — finds torrents on MAM matching user-defined rules and adds them to
   qBittorrent, while protecting the account's ratio, unsatisfied-snatch slots, and freeleech-wedge
   supply.
2. **Library organizer** — watches qBittorrent for finished torrents and links the wanted files into
   a clean `Author/Series/Title` folder tree suitable for Audiobookshelf and similar tools.

## Getting started

Fetchshelf runs in Docker. The [user guide](docs/user-guide.md) walks you through installing it with
Docker or Docker Compose, connecting it to MAM, qBittorrent and Audiobookshelf, and setting up your
library and automatic downloads. For every setting, see the
[configuration reference](docs/configuration.md).

## Status

This project is under active, incremental development.

## Latest release

**2026.10.3** (2026-10-31). See [RELEASES.md](RELEASES.md#2026103---2026-10-31) for what changed, and for
every earlier release.

## Contributing

Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), and see the
[contributors guide](docs/contributors-guide.md) for setting up a development machine and the full workflow.

## License

MIT — see [LICENSE](LICENSE).
