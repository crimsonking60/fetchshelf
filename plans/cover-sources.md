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
   3. **Open Library**, without a key: by ISBN;
   4. **Audnexus** (Audible's data), without a key, for audiobooks with an ASIN.
2. The Search page's details show a cover with `[search] covers = true`, from Hardcover and Open Library (a search
   result has no files yet).
3. The same limits as before: at most `covers_from_mam` covers fetched per Link run (0 to 100), a book looked at once,
   series libraries and books of split collections left out, nothing replaced.
4. Polite use of both services, and nothing sent to them but what they need to find a book.

## The setting's name

`covers_from_mam` no longer says what it does. Decided: a top-level `covers` (0 to 100, default 0) with the same
meaning, and `covers_from_mam` still accepted as its old name, with a warning at start-up to rename it, so released
configurations keep working. Both set is a configuration error.

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
- **`AudnexusCoverSource`** (new `Fetchshelf.Clients.Audnexus`, small): `https://api.audnex.us/books/<asin>`, whose
  `image` is the cover's address on Audible's image host (checked); for audiobooks with an ASIN only. One second between
  requests; 429 ends it for the run.
- **`ChainedCoverSource`**: tries them in order, the first cover wins. A source that throws `CoverSourceRefusedException`
  is skipped for the rest of the run, not the whole chain: the embedded covers still get written.
- Each image is checked (JPEG, PNG or WebP by its first bytes, at most 5 MB), as before.

### 2. Registration and the Link job

- `CoverWriter` is registered with the chain whenever `covers` (or `covers_from_mam`) is above 0.
- **Two budgets per Link run**:
  - **Embedded covers** don't count towards `covers`, as they cost no request; instead each run spends at most about
    10 seconds on them (a time budget, as reading a cover takes milliseconds on a local disk and many times that on a
    NAS, so a count would be wrong on one of them), then stops, and the next run goes on. A large library fills in
    within a few runs, and a Link run never takes more than about 10 seconds longer than now.
  - **Downloaded covers** (Hardcover, Open Library, Audnexus) count towards `covers`, as now: their limits are requests
    per minute.
  - The folders are read without the library write gate, which is taken only for the moment each cover is written, so
    Link, Clean and bulk actions aren't held up by the reading.
- The Link job's result says how many covers were written, and from where: "12 covers written (8 from the files, 3 from
  Hardcover, 1 from Open Library); 40 more waiting".
- Audnexus comes after Open Library for audiobooks; for ebooks it isn't asked.

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

1. Embedded covers and the chain, with the setting's new name and the time budget (no requests at all). One PR.
2. Open Library and Audnexus. One PR. Books step 1 settled without a cover (no source then) are looked at again: a
   data upgrade sets `CoverCheckedAt` back to null for linked books whose folder has no image.
3. Hardcover, and the Search page's covers. One PR.

## Decisions

Settled on 2026-11-22:

1. **The setting's name**: `covers`, with `covers_from_mam` accepted as its old name (see above).
2. **Embedded covers and the limit**: a time budget of about 10 seconds per Link run instead of a count
   (see Registration and the Link job).
3. **Audnexus**: yes, for audiobooks with an ASIN, after Open Library.

## Risks

- **Wrong covers** from a title-and-author match: only exact key matches are taken; an ISBN match is preferred.
- **Rate limits**: both services are asked slowly and at most `covers` times per run; a refusal ends that source for
  the run.
- **Privacy**: titles, authors, ISBNs and ASINs of the user's books go to Hardcover, Open Library and Audnexus. Documented, and only with
  `covers` above 0 (and the token, for Hardcover).

## Verification

- Data tests: the chain's order, a refused source skipped for the run, the per-run limit, embedded covers from each
  format, nothing replaced.
- Client tests against fake servers: Hardcover's query and match rules, the image host check, Open Library's 404 and
  429, the spacing between requests.
- Web tests: the Search page's cover with `[search] covers` on and off; the Config page and `--check-config` with the
  old and new setting names.
