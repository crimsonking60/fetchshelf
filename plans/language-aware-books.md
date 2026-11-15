# One book per language — implementation plan

## Context

Fetchshelf decides "is this the same book?" in two different ways, and they disagree about language.

**Most of Fetchshelf counts language.** `SameBookComparer.AreSameBook` requires the same language, along with the
media type, edition, an author and a narrator in common. Selection (`SelectionRunner`: duplicate of the library, of
a selection), the Clean job's replacement of copies (`DuplicateCopies`) and the Follow job's "already in the library"
check by title all use it. An English and a Greek copy of *Gamma* are two books there, and neither replaces the other.

**The Follow job's grouping and series ownership don't.** In `FollowRunner`:

- `ToCandidate` gives each result a book key: `entry:<series entry>` for a numbered series book, else
  `title:<normalized title>`. `Decide` groups the results by book key and format (audio, ebook), and each group gets
  one find: the first `[[follow.grab]]` block whose filter and cost accept a torrent picks the best one (format, then
  seeders). Language is not part of the key.
- `WithoutOwnedAsync` counts a series entry as owned when the library has it in that format
  (`LibraryQueries.OwnedEntriesAsync`), in any language.

What that does, with the top-level `languages = ["English"]` and these blocks:

```toml
[[follow.grab]]
cost = "free"
categories = { audio = false }
languages = ["Hungarian"]

[[follow.grab]]
cost = "ratio"
categories = { audio = false }
```

- **Series.** *Discworld #1* in Hungarian and in English are one group. The free block takes the Hungarian one only
  when it is free; otherwise the ratio block (which inherits English) takes the English one. 33 Hungarian Discworld
  ebooks on MAM come down to no finds at all: each loses to the English edition of its entry, or its entry is owned
  (in English). In words, the blocks mean "a free Hungarian edition if there is one, else an English one", not
  "English editions, and Hungarian ones when they are free".
- **Single books.** A translation usually has its own title, so it is a book of its own. When the titles match
  (*Gamma* in English and in Greek), the two are one group, and one of them is found. Once that one is selected or in
  the library, the other is no longer the same book by `SameBookComparer`, so the next search finds it: on *Auto*,
  it is downloaded a search later. Both editions arrive, but by accident, and only for books without a series number.

**Linking doesn't count language either.** `FolderNameComputer` builds `Author/Series/Series #n - Title, Edition
{Narrator}`. Two editions with the same title in the same library get the same folder: the second torrent's files are
placed beside the first (as `name (2).epub` when the names match, `FileLinker`), `metadata.json` and `metadata.opf`
hold whichever was linked last, and Audiobookshelf sees one book with two files. Unlink and Clean still work per
torrent, since each records its own files. Today this happens rarely, through the accident above or by hand; once the
Follow job finds every language, it becomes common for books whose title is the same in both languages.

## Goal

A book in two languages is two books everywhere: found, owned, selected, replaced and linked as two. Each
`[[follow.grab]]` block decides for the languages it takes, and no language's edition hides another's.

## Change

### Part 1: the Follow job

1. **The book key includes the language.** `Candidate.BookKey` becomes `<language>|entry:<entry>` or
   `<language>|title:<normalized title>`, the language from the mapped metadata. `Decide` then groups by book,
   language and format, and each group runs through the blocks as today. A block's `languages` filter decides which
   groups it can take; a group no block takes is dropped (as today, counted under "in no grab block's filter").
2. **`prefer_format` within a language.** The "book in both formats keeps one" step groups by book key, which now
   includes the language, so an English audiobook no longer pushes out a Hungarian ebook.
3. **Series ownership per language.** `LibraryQueries.OwnedEntriesAsync(nameKey, side, language)` joins
   `Torrents.Language` (the column exists), and `WithoutOwnedAsync` asks per candidate's language. Owning the English
   *Discworld #1-40* collection no longer counts the Hungarian #1 as owned. The title check already compares language.
4. **Finds know their language.** A `Language` column on `FollowFinds` (migration, nullable), written by `Record`.
   Finds recorded before the migration have none until a search finds them again, which sets it; until then they
   show without one.
5. **The Following pages show it.** A Language column in the find list, shown only when the listed finds are in more
   than one language, and a Language filter next to Format. The follow's counts stay per state, not per language.
6. **The search summary line** (`SearchOutcome.Describe`) says "books (one torrent per book, language and format)".

With the blocks above, after the change: every English Discworld book not owned in English is a find (ratio block);
every Hungarian one not owned in Hungarian is a find too, taken by the free block when it is free, else listed as
*Not free* (the free block's filter takes it, its cost doesn't, and the ratio block doesn't take Hungarian).

### Part 2: library folders

Two editions with the same title need different folders. Three ways, not exclusive:

**A. Mark a language that isn't the first.** A book whose language isn't the first of the top-level `languages` gets
it in the title part: `Author/Gamma (Greek)`, `Author/Discworld/Discworld #1 - A mágia színe (Hungarian)`.

- The same book always gets the same folder, whichever edition was linked first.
- It changes the folder of every book already linked in another language: `LibraryMismatchCalculator` compares each
  linked book's folder with the one it would compute now, so all of them show as *Different folder* until relinked.
  So it has to be opt-in, per library.
- Without a top-level `languages`, there is no first language, and nothing is marked.

**B. Mark only on a clash (always on).** When the plain folder already holds a linked book in another language (a
`Torrents` row with that `LibraryPath` and a different `Language`), the new one gets ` (Greek)`. The recompute in
`SyncMismatch` applies the same rule, so the first edition keeps the plain folder and the second keeps the marked one,
and neither shows as a mismatch.

- No existing folder changes: it fixes the merge without moving anything.
- Which edition gets the mark depends on which was linked first.
- It is a database check during Link, like the narrator fallback's `directoryExists` check, not a pure function of
  the metadata.

**C. A folder template.** `plans/safe-folder-names.md` adds a per-library `folder_template` with a `{language}`
token. That covers a library that wants the language in every folder, but not "only when it isn't the first", and
it doesn't help libraries left on the default layout.

**Recommendation:** B always, as a fix to the merged folder, and A as a per-library setting for those who want stable
names:

| Setting (`[[library]]`) | Default | Description |
|---|---|---|
| `language_in_folder` | `"on_clash"` | `"on_clash"`: a book gets its language in its folder name only when its plain folder already holds the same title in another language. `"other"`: every book not in the first of the top-level `languages` gets it. |

A library holding one language only, which is how Audiobookshelf is usually set up, never sees either.

### Part 3: what stays as it is

- **The Series page** counts missing entries in any language: *Discworld* with English 1–40 owned shows nothing
  missing, even though the Follow job now finds Hungarian ones. A per-language view of the Series page is a separate
  change; noted here so the difference isn't a surprise.
- **Autograb, lists and the Search page** already count language through `SameBookComparer`.
- **The Upgrade job** searches each book in its own language already.
- **Hardcover lists** take one torrent per format for each list book, in any of the blocks' languages, the first
  block that accepts one deciding (`HardcoverListImporter.ProcessItemAsync`), as the Follow job does today. A list
  book is one book someone means to read, so one copy per format is what is wanted there; it stays as it is, and the
  difference is noted in the docs.

## Traps to think through before building

1. **No more fallback between languages.** Today's blocks express "a free Hungarian edition, else an English one",
   and some may want exactly that. After the change they can't: each language is judged on its own. If that wish is
   real, a `[follow]` setting such as `editions = "each_language"` (new) or `"one"` (today's behaviour) keeps both;
   the plan assumes `each_language` only.
2. **Auto downloads more.** A followed series on *Auto* with blocks taking two languages downloads both editions of
   every book. With the blocks above it doesn't (Hungarian is free-only), but a second ratio block for another
   language would double the downloads. The user guide's warning about *Auto* should say so.
3. **Collections across languages.** An English collection *Discworld #1-40* no longer covers the Hungarian books,
   which is the point; but a Hungarian collection of 1–5 covers Hungarian 1–5 only, so English singles 1–5 are still
   found. That follows from the rule, and is what "one book per language" means.
4. **Unknown or wrong languages on MAM.** A torrent MAM files under the wrong language becomes a book of its own in
   that language, where today it would usually lose to the right one. It shows as a find in a language no one
   expects; dismissing it is the remedy.
5. **Existing finds.** After the upgrade, the first search of each follow can list many new finds at once (every
   other-language edition it skipped before). On *Auto*, those the blocks accept are downloaded in that run, within
   the unsatisfied-snatch budget. Running the first searches with `dry_run = true` shows what would happen.
6. **Folder names and Audiobookshelf.** With `"on_clash"`, a later relink can't swap which edition has the mark, but
   removing the first edition and relinking the second moves it to the plain folder (the clash is gone), which
   Audiobookshelf sees as a new item.

## Tests

- `FollowRunnerTests`: an English and a Hungarian edition of one series entry give two finds; owning the English
  collection hides the English singles only; a free-only Hungarian block with a ratio English block lists Hungarian
  as *Not free* and takes English; `prefer_format` keeps one format per language; *Gamma* in two languages gives two
  finds in one search.
- `LibraryQueriesTests`: owned entries per language.
- `FolderNameComputer` tests for `"other"`; `LinkRunnerTests` for the clash rule, both orders, and that the
  recompute flags neither edition as *Different folder*.
- `FollowPagesTests`: the Language column only with several languages, and the Language filter.
- The migration's upgrade test (`IMigrator.Migrate("<previous>")`) with existing finds.

## Docs

- `docs/configuration.md`: `[follow]` (how blocks and languages decide what is found), `[[library]]`
  (`language_in_folder`).
- `docs/user-guide.md`: Following (one find per book, language and format; the Language column), the *Auto* warning,
  "How finished downloads are organized" (folder names for other languages).
- `RELEASES.md`: Upgrading — the first searches can list many other-language finds; `dry_run` to see them first.

## Order

Part 1 and the clash rule of part 2 go together: finding both editions without the clash rule would start merging
folders. `"other"` can come in the same change or later. If `safe-folder-names.md` is built first, `"other"` can be
expressed as part of its template handling, and only the clash rule is new here.
