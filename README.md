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

## Status

This project is under active, incremental development and does not yet have a usable release.

## Latest release

None yet. Every release and its changes are listed in [RELEASES.md](RELEASES.md).

## Contributing

Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), and see the
[contributors guide](docs/contributors-guide.md) for setting up a development machine and the full workflow.

## License

MIT — see [LICENSE](LICENSE).
