# Lists on the Books page and in OPDS

## Problem

A list (`[[hardcover_list]]`, `[[file_list]]`, `[[bookmarks_list]]`) is usually a collection of related books, such as an
award's winners, and is added to get and read them. The Lists page shows what was found on MAM and what became of it,
but nothing shows the list's books that are in the library as a collection to open and read. The Books page browses
by recently added, author, series and library; a list is another such grouping.

## Design

A **Lists** view on the Books page, beside Recently added, Authors, Series and Libraries, and a *Lists* section in the
OPDS catalog. Both show, per configured list, the list's books that are in the library, in the list's order, as the
same book cards and OPDS entries as the other views. The list's own page links its books in the library to them.

1. **Which library book a list's book is** (`ListShelfReader`, `Fetchshelf.Data/Catalog`, beside `BookCatalogReader`).
   Each side (audiobook, ebook) of each list item is resolved to a linked torrent, in order:
   - its MAM id (`AudioMamId`/`EbookMamId`) is a recorded torrent's;
   - else it is a `DuplicateTorrents` row's MAM id: the torrent it is a duplicate of (`DuplicateOfTorrentId`);
   - a torrent replaced by an upgrade (`ReplacedWithTorrentId`) stands for its replacement;
   - else, for a side marked *Existing* or *Selected* without such a torrent, a catalog book in that format with the
     item's normalized title and one of its authors, as `ListImporter.FindLibraryMatchAsync` judges it, in memory
     against the catalog's books rather than one query per item.
   Only torrents in the catalog count (linked, with a chosen file). Items no longer on the list (`RemovedAt`) are left
   out; items marked done are kept, as being read is what marking done often means. A book matched by both sides
   once (one torrent with both formats) is shown once.
   Result: `CatalogList(ListId, Name, IReadOnlyList<string> BookIds, int ItemCount)`, books in the list's order
   (`Position`).

2. **Cached with the catalog.** `BookCatalogCache`'s snapshot also holds the lists, read in the same refresh, so the
   Books page and OPDS don't read every list item per request. `BookCatalog` gets `Lists` and `ByList(listId)`;
   `OpdsCatalog.From` keeps each list's books that are in the OPDS catalog (ebooks of the `[opds]` libraries), and a
   library's scope keeps its own.
   The lists are the configured ones (`ListDefinition.AllOf(settings)`), in configuration order, by their display name;
   a list no longer configured is left out, as the Lists page shows it apart.

3. **Books page.**
   - `View = "lists"`, labelled *Lists*, after *Series*, shown when a list is configured.
   - The view lists the lists with at least one book in the library, as the Series view lists series: name and
     "*n* of *m* books in the library" (*m* the list's books, removed ones left out). The media type filter applies to the
     counts and books, as elsewhere.
   - A list opens its books in the list's order (`List = <list id>`), with a link to the list's own page for what is
     not in the library yet. Search, genre and fiction filters work as on the other views.
   - The list id has a `/` for another user's Hardcover list: it goes in the query string, not the path.

4. **OPDS.**
   - The start feed (whole catalog and each library's) gets *Lists* when at least one list has a book in that scope,
     with `/lists` (a navigation feed, one entry per list with its count) and `/lists/books?id=…` (an acquisition feed in
     the list's order, paged as the others).
   - As the OPDS catalog has only ebooks, a list that collects only audiobooks has no books there and isn't shown: no
     setting is needed for it. A list with both shows its ebooks.

5. **The list's page.** A side whose torrent is in the catalog (as resolved in 1) links *In the library* to the book's
   page on the Books page (`/Books/Detail?id=…`), which links on to the torrent's details, instead of straight to the
   torrent. A side resolved only by title gets the link too, where it now has none.

6. **Docs and README.** The user guide's *Browsing your books* and *Reading on an e-reader (OPDS)* sections, and the
   lists sections where they say what the list's page shows; the README's Untested list gets "The Books page's and
   OPDS's Lists".

## Left out

- A per-list setting to hide a list from the Books page or OPDS. Whether a list shows in OPDS follows from its books:
  an audiobook-only list has none there.
- Lists no longer configured, and the books of a list that aren't in the library: the list's own page has those.
- Ordering lists other than by the configuration's order.

## Tests

- `ListShelfReader`: a side found by MAM id, by a duplicate, through an upgrade's replacement, and by title; a removed
  item left out; a book with both formats shown once; the list's order kept; an item whose torrent isn't linked left
  out.
- `OpdsCatalog`: an audiobook-only list has no books and no entry; a mixed list keeps its ebooks; a library's scope
  keeps its own.
- Books page: the Lists view's groups and counts, a list's books in order with the media type filter, a list id with a
  `/`, no Lists choice without a configured list.
- OPDS: the start feed's *Lists* entry only when a list has an ebook, the lists feed and a list's books feed.
- Lists page: *In the library* links to the book's page, also for a side matched by title.
