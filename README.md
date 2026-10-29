<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/logo-dark.svg">
    <img src="docs/images/logo-light.svg" alt="" height="32" align="bottom">
  </picture>
  Fetchshelf
</h1>

Fetchshelf is a self-hosted service that connects the private book tracker
[MyAnonamouse](https://www.myanonamouse.net) (MaM) with a BitTorrent client (qBittorrent) and,
optionally, [Audiobookshelf](https://www.audiobookshelf.org). It is a C# reimplementation of
[MLM](https://en.wikipedia.org/wiki/MyAnonamouse) ("MyAnonamouse Library Manager"), an abandoned Rust
application, built from its functional specification (`Fetchshelf-spec.md`).

Fetchshelf has two independent, optional halves:

1. **Auto-downloader** — finds torrents on MaM matching user-defined rules and adds them to
   qBittorrent, while protecting the account's ratio, unsatisfied-snatch slots, and freeleech-wedge
   supply.
2. **Library organizer** — watches qBittorrent for finished torrents and links the wanted files into
   a clean `Author/Series/Title` folder tree suitable for Audiobookshelf and similar tools.

See `Fetchshelf-spec.md` for the full behavioral specification, 
this document was created by analyzing the original MLM implementation.

## Getting started

Fetchshelf runs in Docker. The [user guide](docs/user-guide.md) walks you through installing it with
Docker or Docker Compose, connecting it to MaM, qBittorrent and Audiobookshelf, and setting up your
library and automatic downloads. For every setting, see the
[configuration reference](docs/configuration.md).

## Status

This project is under active, incremental development.

## Latest release

**2026.10.2** (2026-10-29). See [RELEASES.md](RELEASES.md#2026102---2026-10-29) for what changed, and for
every earlier release.

## Contributing

Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), and see the
[contributors guide](docs/contributors-guide.md) for setting up a development machine and the full workflow.

## License

MIT — see [LICENSE](LICENSE).
