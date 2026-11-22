# Genre support: fiction/nonfiction and MAM's genres — implementation plan

Not an issue yet: written 2026-11-21 from a request in conversation.

## Context

Every MAM search result carries two classifications of a torrent besides its media type and its old category:

- **The main category,** `maincat` in the search JSON: 1 Fiction, 2 Nonfiction. Parsed by `SearchResponseParser`
  (`FictionCategoryId`) and stored as `TorrentMetadata.FictionCategory` (`FictionCategory?`, null when MAM sends
  anything else).
- **The genres,** `categories` in the search JSON: a list of ids, which MAM's own pages call "categories". Parsed as
  `GenreCategoryIds` and stored as `TorrentMetadata.GenreCategories`, a set of the `GenreCategory` enum (62 ids,
  numbered as MAM numbers them; ids added later show as "Unknown Category (id: N)").

Both are in the stored metadata of every torrent whose metadata came from MAM, since the whole `TorrentMetadata` is
serialized into `MetadataJson`. Neither is used for anything but the genre list written to `metadata.json`,
`metadata.opf`, Audiobookshelf and the OPDS catalog (`LibraryMetadataJsonBuilder.BuildGenres`), which leaves the
fiction dimension out altogether.

The two are **independent dimensions**: a *Historical* book can be fiction (a historical novel) or nonfiction (a
history). Anything that selects by genre must be able to say both, as in "nonfiction Historical" or "fiction, any
genre but Romance".

### What MAM offers, and what it doesn't

MAM's search request (`/tor/js/loadSearchJSONbasic.php`, `SearchRequestDto`) filters by media type (`main_cat`), old
category (`cat`), language and the rest, but **not by genre or by fiction/nonfiction**. Filtering search results by
genre therefore happens on the results Fetchshelf receives. Tested on 2026-11-22: `tor[categories]` (genre ids, as
MAM's own search form sends them to another endpoint) is silently ignored, and `cat` filters by the old category, not
the genre (`cat: [42]` gave Audiobooks - General Fiction, not Romance). No other MAM endpoint is needed or may be used: everything
in this plan uses data already in the search results.

### Names

The genre names Fetchshelf shows come from the enum's member names (`GenreCategoryDisplay`): *Action* for MAM's
*Action/Adventure*, *Lgbt* for *LGBTQIA+*, *Drama Plays* for *Plays/Drama*, *Occult* for *Occult / Metaphysical
Practices*, *General Fiction* for *Literary Fiction*, *Contemporary* for *Contemporary Fiction*, and the first word only
for paired names such as *Business/Money*. Configuration that names genres needs names people can copy from MAM.

## Goals

1. Fiction/nonfiction and the genres are both available, as two dimensions, everywhere below.
2. **Libraries by genre:** a library can take only books of certain genres and/or of fiction or nonfiction, so books
   arriving in one qBittorrent category or download folder are split between libraries.
3. **Search page:** filter the results by genre and by fiction/nonfiction.
4. **Torrents page:** filter the list by genre and by fiction/nonfiction.
5. **All of it optional,** behind one top-level setting. With it off, Fetchshelf behaves, looks and writes files
   exactly as now.

## The switch

A top-level boolean, `genres`, default `false`:

```toml
genres = true
```

With `genres = false` (or unset):

- no genre or fiction filter appears on any page, and nothing shows the fiction dimension;
- a `[[library]]` using `allow_genres`, `deny_genres` or `fiction` is a configuration error that names the missing
  `genres = true` ("… needs genres = true"), so a library split isn't silently ignored;
- genre names in `metadata.json`, `metadata.opf`, Audiobookshelf and OPDS stay as they are now (open question 2).

What is kept regardless of the switch: the derived genre data in the database (step 1). It is cheap, written on save
like the existing derived tables, and having it always there means turning `genres` on needs no rebuild or upgrade
step. It is invisible with the switch off.

## Configuration

### Naming a genre

Genres are named by **MAM's labels** as on MAM's search form, compared ignoring case, spaces and punctuation:
`"Action/Adventure"`, `"action / adventure"` and `"actionadventure"` are the same genre; `"LGBTQIA+"` is `lgbtqia`.
A name that matches no genre stops Fetchshelf from starting, listing the valid names. A `GenreLabels` table in Core
gives each `GenreCategory` its MAM label and its normalized key; the enum's member names stay as they are, since stored
metadata holds them (`JsonStringEnumConverter`).

The fiction dimension takes `"fiction"` or `"nonfiction"`.

### `[[library]]`

| Setting | Default | Description |
|---|---|---|
| `allow_genres` | `[]` | If not empty, the book must have at least one of these genres. |
| `deny_genres` | `[]` | A book with any of these genres is not taken. |
| `fiction` | unset | `"fiction"` or `"nonfiction"`: the book's main category must be this one. Unset takes both, and books MAM gave neither. |

They follow `allow_tags` and `deny_tags`, which work the same way on qBittorrent tags. All conditions of a library
must hold: its category or download folder, its tags, its genres and its fiction setting. Libraries are tried in the
order of the file, and the first that takes the book gets it, as now. A library without genre conditions after the
genre libraries takes the rest:

```toml
genres = true

[[library]]
name = "History"
category = "ebooks"
library_dir = "/books/history"
fiction = "nonfiction"
allow_genres = ["Historical", "Politics/Sociology"]

[[library]]
name = "Historical fiction"
category = "ebooks"
library_dir = "/books/historical-fiction"
fiction = "fiction"
allow_genres = ["Historical"]
deny_genres = ["Romance"]

[[library]]
name = "Ebooks"
category = "ebooks"
library_dir = "/books/ebooks"
```

A book with **no genres** (metadata entered by hand, or from before genres were stored) matches no `allow_genres` and
no `deny_genres`; a book with **no main category** matches no `fiction` setting. Such books go to a library without
those conditions.

Validation: each name in `allow_genres`/`deny_genres` must be a genre; a genre in both lists of one library is an
error; `fiction` must be one of the two values. `5-everything.toml` gets `genres = true` and a library using all
three (`ExampleConfigTests`).

## Approach

### 1. Genre data in the database

- **`TorrentGenres`** (`TorrentId`, `Genre` as the MAM id), a derived table like `TorrentPeople` and `TorrentSeries`,
  and a **`FictionCategory`** column on `Torrents` like `Language` and `MediaType`. Both are rewritten by
  `FetchshelfDbContext` on save from `MetadataJson`, and by nothing else.
- An EF migration adds them, and a numbered data upgrade (`DatabaseMigrator`, SQLite `user_version`) fills them for the
  existing rows. Going back to an older version afterwards is not possible, as with every database upgrade; the
  release notes' **Upgrading** says so.
- Indexed for the Torrents page's filters at 10,000 torrents (`PageScaleTests`).

### 2. Core: genres, labels and matching

- `GenreLabels` (Core/Metadata): MAM's label for each `GenreCategory`, the normalized key, and `TryParse(name)`.
- `GenreCondition` (Core/Organizer): `AllowGenres`, `DenyGenres`, `Fiction`, and `Matches(TorrentMetadata)` with the
  rules above. Pure, unit-tested in Core, including the two-dimension cases (nonfiction *Historical* taken by History,
  fiction *Historical* by Historical fiction, fiction *Historical Romance* by neither).
- `LibraryMatchRule` gets the condition. `LibraryMatcher` splits into two questions: which libraries could take the
  torrent by category, folder and tags (no metadata needed), and which of those takes the book (`Matches`).

### 3. The Link job

The library is matched **before** the MAM lookup today, so a torrent no library claims costs no request. That stays:

1. Candidates by category, download folder and tags, as now. None: ignored, as now, with no MAM request.
2. The metadata is looked up or read as now.
3. The first candidate whose genre condition takes the book is the library. A candidate without conditions always
   does. When none does, the torrent is not linked and the Errors page says so (a new reason, "No library takes its
   genres", naming the book's fiction category and genres), until the configuration or the metadata changes.

The same choice applies wherever a book's library is worked out again:

- **Already linked books** (`SyncMismatchAsync`): a book whose genres now pick another library, after a **Refresh**
  or an edit, or after the configuration changed, is shown as *Different library*, like a changed qBittorrent category
  today (see "When the folder no longer matches" in the user guide). A relink moves it; after a new split, the
  Torrents page's **Problem** filter and the bulk **Relink** move them all at once.
- **Split collections:** each book takes the collection's genres and main category, so a collection's books stay in
  one library.
- **Relink, Unlink and the torrent page's library line** use the same choice.

Nothing at download time needs to know the library: the qBittorrent category is chosen as now (`[[tag]]` rules, a
block's `category`, **Download into**), and per-library formats apply when linking (`LibraryCopyRank`).

### 4. Torrents page

With `genres = true`, two more filters beside **Language** and **Media type**:

- **Genre:** one genre, or *No genre*. Queried through `TorrentGenres`.
- **Fiction:** *Fiction*, *Nonfiction* or *Neither*. Queried on the `FictionCategory` column.

Both keep the page's existing behaviour: in the address, in the filter summary, cleared by **Clear filters**, combined
with the other filters and the search, and kept by the export links.
A torrent's own page shows its main category and genres (MAM's labels) with the other metadata.

### 5. Search page

With `genres = true`, a **Genre** and a **Fiction** filter in the search form, applied to the results MAM returns,
since MAM can't filter by them:

- A page of results is fetched as now, then filtered; the page says how many of its results the genre filters hid
  ("12 of 100 results on this page hidden by the genre filters"), so a short page isn't mistaken for the end.
- Paging stays MAM's: the next page is MAM's next page, filtered the same way. No extra MAM requests are made to fill
  a page (open question 3).
- The filters are in the address with the rest of the search, so the result page's **Back to the search** keeps them.
- The result's details already list its genres (as *Categories*); they get MAM's labels and the main category.

### 6. Metadata, hand edits and display (with the switch on)

- The torrent **Edit** page gets the main category (*Fiction*, *Nonfiction*, unset) and the genres (a list of
  checkboxes in MAM's labels), so a book entered by hand, or one MAM classified oddly, can be placed. An edited book
  counts as edited by hand, as now, so a bulk **Refresh** leaves it alone.
- Shown names use MAM's labels wherever genres appear in the UI.

### 7. Documentation

- `docs/configuration.md`: `genres` among the top-level settings; `allow_genres`, `deny_genres` and `fiction` in the
  `[[library]]` table and its rules; the list of genre names as MAM labels them.
- `docs/user-guide.md`: a section "Sorting books by genre" (the two dimensions, an example split, what happens to a
  book no library takes, moving books after changing the split), the new filters under Torrents and Search, and the
  Errors page's new reason.
- `RELEASES.md` at release time: the database upgrade under **Upgrading**.

## Order of work

1. Step 1 and the Core part of step 2 (`GenreLabels`, `GenreCondition`), with the database upgrade. No visible change.
2. The `genres` switch, the library settings and their validation, and the Link job (steps 2 and 3), with the docs for
   them.
3. The Torrents page filters and the torrent page's genre lines (step 4).
4. The Search page filters (step 5).
5. The Edit page and the labels in the UI (step 6), then the rest of the docs.

One feature branch and PR per step.

## Open questions

1. **The shared torrent filter.** `[[autograb]]` blocks, `[[tag]]` rules and the list and follow searches use the shared
   torrent filter (`TorrentFilter`). Adding `genres`, `not_genres` and `fiction` to it would let automatic downloads
   skip or pick genres, and let `[[tag]]` rules send genres to different qBittorrent categories. Not asked for, so left
   out; *proposed:* a later step on the same switch if wanted.
2. **Genre names in files and Audiobookshelf.** `metadata.json`, `metadata.opf`, Audiobookshelf and OPDS get the
   shortened names today. *Proposed:* with `genres = true` they get MAM's labels and *Fiction* or *Nonfiction* as one
   more genre; with it off, unchanged. Files already written keep the old names until they're written again (a
   **Refresh and relink**, or the next change to the book).
3. **Search paging.** Filtering one MAM page can leave few results. *Proposed:* filter only the page fetched, and say
   how many were hidden. The alternative, fetching more pages to fill one, costs MAM requests the user didn't ask for.
4. **The Books page.** A genre and fiction filter there too, from the catalog (no database query needed)? Not asked
   for; *proposed:* in step 3, as it is small once the labels exist.

## Risks

- **Books no library takes.** A split with no catch-all library leaves books unlinked. The Errors page reason, and an
  example with a catch-all library in the docs, make this visible rather than silent.
- **MAM's classification is uneven.** Older uploads may lack genres or the main category, and some are classified
  oddly; the "no genre" and "neither" handling and the Edit page are the answers.
- **Labels change on MAM.** A renamed or new genre on MAM needs a code change, as now; an unknown id still shows as
  "Unknown Category (id: N)" and matches no named genre.
- **Moving files.** A changed genre condition can mark many books as no longer matching. Nothing moves without a
  relink, so nothing happens behind the user's back.

## Verification

- Core tests: label parsing (case, spaces, punctuation, unknown names), `GenreCondition` in both dimensions, books
  without genres or main category, library choice among candidates.
- Data tests: the derived table and column on save and after the data upgrade; the Link job placing books by genre,
  making no MAM request when no candidate exists, the Errors reason when no candidate takes the book, mismatch after a
  genre change, split-collection books following the collection.
- Config tests: the switch, each setting, unknown names, a genre in both lists, settings without `genres = true`;
  `ExampleConfigTests`.
- Web tests: the Torrents filters (with search, other filters and export), the Search filters and
  the hidden count, the Edit page, everything absent with the switch off.
- `PageScaleTests`: the Torrents page's genre and fiction filters at 10,000 torrents.
