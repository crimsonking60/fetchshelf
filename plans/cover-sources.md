# Cover sources other than MAM — implementation plan

Not an issue yet: written 2026-11-22, after MAM's image server began refusing Fetchshelf.

## Context

Since 2026-11-22, MAM's image server answers every cover request from Fetchshelf with 403 "Agent Miss-Match", the
session otherwise working as before. The image server was never among the parts of MAM its API documentation allows
automating; the refusal is taken as MAM saying no. PR #185 removed every call to it:

- the Link job's covers (`covers_from_mam`, `CoverWriter`) now take an `ICoverSource` (Data/Organizer) and run only
  when one is registered: none is, so no covers are fetched;
- the Search page's result details show no cover, and `[search] covers` (default `false`) is there to show one from the
  same sources once they exist.

This plan adds the sources. Nothing in it calls MAM.

## Goals

1. A book folder without an image gets a cover again, from, in this order:
   1. **its own files**: the cover embedded in an EPUB, CBZ/CBR, m4b, m4a or mp3, written out as `cover.jpg` (or
      `.png`, `.webp`), at no request cost;
   2. **Hardcover**, with the `[hardcover]` token when one is configured: by ISBN, then by title and author;
   3. **Open Library**, without a key: by ISBN.
2. The Search page's details show a cover with `[search] covers = true`, from Hardcover and Open Library (a search
   result has no files yet).
3. The same limits as before: at most `covers_from_mam` covers fetched per Link run (0 to 100), a book looked at once,
   series libraries and books of split collections left out, nothing replaced.
4. Polite use of both services, and nothing sent to them but what they need to find a book.

## The setting's name

`covers_from_mam` no longer says what it does. *Proposed:* a top-level `covers` (0 to 100, default 0) with the same
meaning, and `covers_from_mam` still accepted as its old name, with a warning at start-up to rename it, so released
configurations keep working. Both set is a configuration error. (Open question 1.)

## Approach

### 1. `ICoverSource` implementations (Data, with the clients)

- **`EmbeddedCoverSource`**: reads the book's linked files in its library folder with `BookCoverExtractor` (already used
  by the OPDS catalog and the Books page; extend it to m4b/m4a/mp3 if not there yet — the Books page reads those, see
  `CatalogBook`). No request. Skipped for a `no_link` library, whose folder has no files.
- **`HardcoverCoverSource`** (`Fetchshelf.Clients.Hardcover`): a GraphQL query for the book's cover image
  (`image { url }`), first by ISBN-13/ISBN-10 through editions, then by title and first author, taking a match only when
  the title and an author compare equal by Fetchshelf's own keys (`TitleNormalizer`, `PersonNameKey`), so a wrong
  edition or namesake isn't taken. The image is then downloaded from the address Hardcover gives, which must be
  Hardcover's own image host (checked, as `IsMamAddress` did for MAM). Only with a `[hardcover]` token.
  - Rate: Hardcover allows about 60 requests a minute; the client keeps one second between queries.
- **`OpenLibraryCoverSource`** (new `Fetchshelf.Clients.OpenLibrary`, small): `https://covers.openlibrary.org/b/isbn/<isbn>-L.jpg?default=false`,
  which answers 404 when it has none. Only for books with an ISBN.
  - Rate: Open Library limits ISBN lookups to about 100 per 5 minutes per IP; the client keeps three seconds between
    requests, and stops the run on 429 (`CoverSourceRefusedException`).
- **`ChainedCoverSource`**: tries them in order, the first cover wins. A source that throws `CoverSourceRefusedException`
  is skipped for the rest of the run, not the whole chain: the embedded covers still get written.
- Each image is checked (JPEG, PNG or WebP by its first bytes, at most 5 MB), as before.

### 2. Registration and the Link job

- `CoverWriter` is registered with the chain whenever `covers` (or `covers_from_mam`) is above 0. Embedded covers always
  count towards the per-run limit too, so a large library fills in over several runs as before; *or* (open question 2)
  embedded covers don't count, as they cost no request.
- The Link job's result says how many covers were written, and from where: "12 covers written (8 from the files, 3 from
  Hardcover, 1 from Open Library); 40 more waiting".

### 3. The Search page

- With `[search] covers = true`, a result's details show the cover from Hardcover, then Open Library, by the result's
  ISBN, title and author, through a handler on the details page as before (`OnGetCover`), cached by the browser for a
  day and in memory for an hour, so opening the same result twice asks once.
- No request is made with the setting off, which stays the default.

### 4. Docs

- `docs/configuration.md`: `covers` (and `covers_from_mam` as its old name), `[search] covers`.
- `docs/user-guide.md`, Covers: where covers come from, in which order, the limits, that Hardcover needs its token, and
  what is sent (an ISBN, or a title and author); Security and privacy's "What leaves your machine" gains Open Library
  and, for covers, Hardcover.
- `RELEASES.md` at release time: covers from MAM are gone, what replaces them, the setting's new name.

## Order of work

1. Embedded covers and the chain, with the setting's new name (no requests at all). One PR.
2. Open Library. One PR.
3. Hardcover, and the Search page's covers. One PR.

## Open questions

1. **The setting's name**: `covers` with `covers_from_mam` as an old name, as proposed, or keep `covers_from_mam`?
2. **Embedded covers and the limit**: count towards the per-run limit (simple, as proposed), or not (faster fill-in)?
3. **Audiobooks**: Audnexus (Audible data, by ASIN) gives good audiobook covers; many MAM uploads have no ASIN. Add it
   as a fourth source later, or not at all?

## Risks

- **Wrong covers** from a title-and-author match: only exact key matches are taken; an ISBN match is preferred.
- **Rate limits**: both services are asked slowly and at most `covers` times per run; a refusal ends that source for
  the run.
- **Privacy**: titles, authors and ISBNs of the user's books go to Hardcover and Open Library. Documented, and only with
  `covers` above 0 (and the token, for Hardcover).

## Verification

- Data tests: the chain's order, a refused source skipped for the run, the per-run limit, embedded covers from each
  format, nothing replaced.
- Client tests against fake servers: Hardcover's query and match rules, the image host check, Open Library's 404 and
  429, the spacing between requests.
- Web tests: the Search page's cover with `[search] covers` on and off; the Config page and `--check-config` with the
  old and new setting names.
