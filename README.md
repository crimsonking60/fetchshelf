# Fetchshelf

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

## License

MIT — see [LICENSE](LICENSE).
