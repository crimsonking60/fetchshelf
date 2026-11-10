# Same-book checks: title keys for every script, and author names compared loosely — implementation plan

## Context

Two torrents count as the same book when their **normalized title keys** are equal and `SameBookComparer` agrees
(same media type, language, edition, at least one shared author, compatible narrators). The key comes from
`TitleNormalizer.Normalize` (`src/Fetchshelf.Core/Metadata/TitleNormalizer.cs`) and is used:

- at selection, against queued selections and the library (`SelectionRunner`, via the stored
  `NormalizedTitleKey` columns of `Torrents` and `SelectedTorrents`), to skip duplicates;
- in the Clean job, to group linked copies of one book and retire all but the best (`DuplicateCopies`);
- by the Link job, the Download job and the snatchlist and Hardcover importers when they store records.

### Titles in non-Latin scripts

`Normalize` strips diacritics, then **drops every character outside ASCII** (`TransliterateToAscii`), then removes
anything that isn't `a-z`, `0-9`, `_` or a space. A title written entirely in Cyrillic, Greek, Arabic, Hebrew,
Chinese, Japanese or Korean becomes an empty key, or just its digits. All such titles share that key, and
`SameBookComparer` deliberately ignores titles, so two different books by the same author in the same language and
media type are taken for the same book:

- at selection, the second is skipped as a duplicate of the first and never downloaded;
- in the Clean job, one of two linked books is **retired from the library** in favour of the other.

Mixed titles are affected more mildly: only their Latin part counts ("Война и мир (War and Peace)" keys as
"war and peace"). Latin letters with no decomposition (`ø`, `æ`, `ß`, `ł`, `đ`, `þ`, `œ`) are dropped too:
"Søren" keys as "sren".

### Author names

`SameBookComparer.ShareAtLeastOne` compares author (and narrator) names with ordinal, case-sensitive equality.
"J. R. R. Tolkien" and "J.R.R. Tolkien", or "Ursula K. Le Guin" and "Ursula K. LeGuin", are different authors, so
real duplicates are downloaded and kept twice. MAM's names are mostly consistent, but uploads differ, and
Fetchshelf already normalizes name casing (`MamMetadataCleaner.NormalizeNameCasing`) without comparing loosely.

## Change

### 1. A title key for every script

In `TitleNormalizer.Normalize`, the first step becomes `TextFolding.Fold` (new, in `Fetchshelf.Core.Metadata`),
which walks the title by `Rune` so letters outside the Basic Multilingual Plane (rare CJK ideographs) are kept whole:

- ASCII stays as it is.
- A Latin letter with accents loses them, as today ("é" → "e").
- The Latin letters that have no decomposition are spelled out: `ø→o`, `æ→ae`, `œ→oe`, `ß→ss`, `ł→l`, `đ→d`,
  `þ→th`, `ð→d`, `ı→i`, and their capitals. So "Søren" and "Soren" still key alike.
- Every other letter or digit is kept **exactly as written, marks included**. Taking marks off outside Latin would
  merge letters that are different: Cyrillic "й" and "и", Japanese "が" and "か". A false merge can retire a book
  from the library, a missed one only keeps two copies, so other scripts err on the side of keeping apart.
- Anything else outside ASCII (typographic punctuation, symbols) is dropped, as today.

The later step that removes everything but `a-z`, `0-9`, `_` and spaces keeps letters and digits of any script
instead, also by `Rune` (a regular expression would see a supplementary-plane letter as two surrogates and drop
it). The leading-article, "volume"/"vol" and " & " rules are unchanged and still only apply to English words.

For a title made only of ASCII and decomposable Latin letters the key is exactly what it is today; the existing
`TitleNormalizerTests` cases stay as they are and prove it.

**An empty key.** A title of only punctuation or symbols still normalizes to an empty key. Instead of returning it,
`Normalize` falls back to the title itself, trimmed and lower-cased invariantly, so only an identical title can
match. A title that is empty or whitespace keys as an empty string, and `DuplicateCopies` and `SelectionRunner`
treat an empty key as matching nothing.

### 2. Author and narrator names compared loosely

A new `PersonNameKey.Of(string name)` in `Fetchshelf.Core.Metadata`: fold as above, lower-case invariantly, and
drop everything but letters and digits of any script. A name with nothing left matches nobody. "J. R. R. Tolkien", "J.R.R. Tolkien" and
"j.r.r. tolkien" all become `jrrtolkien`; "Le Guin" and "LeGuin" become `leguin`.

`SameBookComparer.ShareAtLeastOne` and `NarratorsAgree` compare these keys instead of the raw names. The names
shown and stored don't change.

Not handled, on purpose: name order ("Tolkien, J.R.R."), initials against full names ("John Ronald Reuel
Tolkien"), and pen names. Each would need a fuzzy rule with its own false matches.

### 3. Re-keying stored records at upgrade

Changing the key without updating the stored ones would leave existing records keyed the old way, and a new
selection of a non-Latin title would no longer find its stored duplicate (or would still match the wrong one).
The keys are recomputed once, automatically:

- `DatabaseMigrator.Migrate` reads SQLite's `PRAGMA user_version` after `Database.Migrate()`. EF Core doesn't use
  it (it tracks migrations in `__EFMigrationsHistory`), so it can number data upgrades. Version 0 is today's
  databases.
- Below version 1: recompute `NormalizedTitleKey` for every `Torrents` row from its `Title`, and for every
  `SelectedTorrents` row from the title in its `MetadataJson`, in batches with the change tracker cleared between
  them (as `LinkRunner` does for large libraries), in one transaction; then set `user_version = 1`.
- This replaces what `--update-search-title` was for. The flag stays and runs the same recompute (now including
  selections) for anyone who wants to run it by hand.
- On 10,000 torrents this is one pass over a string column and a few seconds of startup, once. It is logged:
  `Recomputed title keys of 10,214 torrents and 37 selections for the new title rule`.

The Errors page's **Retry download** restores a selection from a stored payload that includes its key
(`GrabberErrorRestorePayload.NormalizedTitleKey`); restoring now recomputes the key from the payload's metadata
instead of trusting the stored one.

## What users will notice

- Books with titles in non-Latin scripts are no longer taken for each other. Ones skipped as duplicates before can
  now be selected; nothing previously retired comes back by itself, but it can be relinked from its torrent page.
- Copies of one book whose author is spelled with different punctuation, spacing or case are now found as
  duplicates: at selection the second is skipped, and **the next Clean run retires the worse of two such copies
  already in the library**, as it does for any duplicate. The **Duplicates** page and the Events page show what it
  did.

## Tests

- `TitleNormalizerTests`: the existing cases unchanged; titles in Cyrillic, Greek, Hebrew, Arabic, Chinese,
  Japanese and Korean key to non-empty, distinct keys and two different titles in one script differ; a mixed title
  keeps both parts; `ø æ œ ß ł đ þ ð` transliterate; a supplementary-plane ideograph is kept; a title of only
  symbols keys as itself; an empty title keys as empty.
- `PersonNameKeyTests`: punctuation, spacing, case and diacritics variants agree; different names differ;
  Cyrillic names keep their letters.
- `SameBookComparerTests`: authors and narrators that differ only in punctuation or case match.
- `DuplicateCopiesTests`: two linked Russian ebooks by one author with different titles are not copies of each
  other; two with the same title are; an empty key groups with nothing.
- `SelectionRunner` tests: a non-Latin title isn't skipped as a duplicate of a different non-Latin title by the same
  author.
- `DatabaseMigrator` tests: a database at `user_version` 0 with old keys is re-keyed and moves to 1; a database at 1
  is left alone; selections are re-keyed from their metadata.

## Documentation

`docs/user-guide.md`, where duplicates and "same book" are explained: titles in any script are compared, and
author names are compared ignoring case, spacing and punctuation. Mention that the first start after the upgrade
recomputes the stored keys.

## Release notes

Under **Fixed**: books with titles in non-Latin scripts were taken for duplicates of each other, so one could be
skipped or retired from the library; author names differing only in punctuation or case weren't recognized as the
same author. Under **Upgrading**: the first start recomputes the title key of every stored torrent and selection,
which takes a few seconds on a large library; the next Clean run may retire copies newly recognized as duplicates.
Back up the database first, as for any upgrade. Going back to an earlier version afterwards leaves the new keys in
place, which the old version reads without problems but compares the old way until its own `--update-search-title`
is run. Written at release time.

## Out of scope

- Fuzzy title matching (subtitles, series prefixes, translated titles).
- Author name order, initials and pen names.
- The Hardcover list matching, which already compares titles and authors fuzzily and isn't affected by the key.
