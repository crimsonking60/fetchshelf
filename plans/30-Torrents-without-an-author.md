# Torrents without an author: Edit from the Errors page, author from the title — implementation plan (#30)

## Context

The Link job refuses a torrent without an author ("Torrent has no author; cannot be linked.") and, for a
torrent added to qBittorrent outside Fetchshelf, stops without creating a Torrent record. Without a record the
torrent is not on the **Torrents** page, **Edit** is out of reach, and the Errors page has no action for it.

The case that prompted the issue: the MAM upload has an empty author field, and the author was put in the
title instead ("Exploitation Film Posters by Graham Marsh").

## Part 1: Edit from the Errors page

**Keep what MAM said.** When the Link job records the no-author error for a torrent with no record, it stores
the metadata it just looked up in the error row's `DisplayMetadataJson` (already used for the Download job's
restore payload). No new column, no migration, and the Errors and Edit pages need no MAM request.

**Errors page.** A Link error whose torrent has no record and whose row carries metadata gets an **Edit**
button, opening `/torrents/{hash}/edit`. Rows recorded before the upgrade get it after the next Link run, which
updates them.

**Edit page.**

- GET: with no record, prefill the form from the Link error's stored metadata, when there is one.
- POST: apply the form onto the metadata it was opened with (the record's, else the error's), instead of
  building metadata from the form fields alone. MAM id, upload time, description, ISBN/ASIN, file types, size,
  VIP status, edition and genres survive the edit. This also changes editing an existing record: until now
  saving dropped every field the form doesn't show, which, among other things, emptied `metadata.json`'s
  description and lost the file types.
- After saving, the next Link run finds the record and links it, as it already does for hand-edited records.

## Part 2: Author from the title

In `MamTorrentMapper.Map`, when MAM gives no authors, take the author from a title ending in ` by <Name>`;
the existing `StripAuthorReference` then removes it from the title. This covers search results, autograbs,
Hardcover lists, snatchlists and Link lookups alike, since they all map through `MamTorrentMapper`.

The rule lives in Core (`MamMetadataCleaner.AuthorFromTitle`) and matches only the clear-cut case:

- The last ` by ` in the title, with a non-empty title before it.
- The name is 2 to 4 words. A single word ("Stand by Me") is never taken.
- Every word starts with an upper-case letter, except name particles (`de`, `van`, `von`, `der`, `le`, `la`,
  `du`, `da`, `di`, `del`), which may not be the first or last word. Initials such as `J.R.R.` count as words.
- No digits, and no characters other than letters, `.`, `'` and `-`.

The leading `<Name> - <Title>` form is left out: it can't be told apart from `<Series> - <Title>`.

The metadata source stays MAM; the author shows on the torrent page and in events like any other. A wrong
guess ("Death by Chocolate Cake") is still visible and can be fixed with **Edit**; it only ever happens for
torrents that otherwise could not be linked at all.

## Tests

- Core: `AuthorFromTitle` accepts the issue's example, initials and particles, and rejects single words,
  lower-case words, digits, a missing title part and a title without ` by `.
- Mam: `MamTorrentMapper` takes the author from the title only when MAM gives none, and strips it from the
  title.
- Data: the Link job stores the looked-up metadata on a no-author error for an unknown torrent.
- Web: the Errors page shows **Edit** for such a row and not for a row with a record; the Edit page prefills
  from the error and keeps the stored fields (MAM id, description) on save; editing an existing record keeps
  its description.

## Documentation

`docs/user-guide.md`: the Errors page section, the folder-naming note on torrents without an author, the
**Edit** description and the "No author" troubleshooting entry.
