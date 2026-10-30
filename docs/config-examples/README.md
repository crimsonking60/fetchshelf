# Example configurations

Six annotated `config.toml` files, ordered from the simplest to the most complete. Pick the one closest
to what you want, copy it to your config location as `config.toml`, replace the `replace-with-...`
placeholders with your own values and adjust it from there.

Every setting is described in the [configuration reference](../configuration.md), and each example
points back to it.

| File | What it shows |
|---|---|
| [`1-minimal.toml`](1-minimal.toml) | The smallest useful config: `mam_id`, one qBittorrent, free bookmarks, and one library matched by `download_dir`. |
| [`2-library-organizer.toml`](2-library-organizer.toml) | The library organizer on its own, with no autograb. Covers format lists, `path_mapping`, `on_cleaned`, link methods and Audiobookshelf. |
| [`3-downloader.toml`](3-downloader.toml) | Bookmarks at three cost tiers, safety limits, `max_active_downloads`, and tag rules that feed category-based libraries. |
| [`4-targeted-searches.toml`](4-targeted-searches.toml) | Author search with `max_authors`, a manual dry-run backfill, freeleech filters, uploader and `mine` types, and snatchlists. |
| [`5-lists-and-audiobookshelf.toml`](5-lists-and-audiobookshelf.toml) | Notion lists, ordered `grab` blocks, per-list overrides, and Audiobookshelf. |
| [`6-everything.toml`](6-everything.toml) | Every setting, including all shared-filter fields, every autograb `type` and cost, and every link method. |

## Which one should I start with?

- **I only want my downloads sorted into a tidy library:** start with `2-library-organizer.toml`.
- **I want bookmarked books downloaded and sorted automatically:** start with `1-minimal.toml`, then see
  `3-downloader.toml` for cost tiers and categories.
- **I want to follow an author or search for new content:** see `4-targeted-searches.toml`.
- **I keep a reading list in Notion:** see `5-lists-and-audiobookshelf.toml`.
- **I want to look up what an option does:** read `6-everything.toml`, which is meant to be read rather
  than copied.

## Things to know

- Each example builds on the one before it, so you can follow them in order.
- The files contain placeholder credentials. Fetchshelf starts with them, but nothing can reach MaM,
  qBittorrent or Audiobookshelf until you fill in real values. Never commit your real `config.toml`.
- Example 6 sets many options that overlap on purpose, so each one can be shown. It is not a sensible
  config to run as it is.

