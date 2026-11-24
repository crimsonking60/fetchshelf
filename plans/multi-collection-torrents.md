# Multi collection torrents: split collections of several series, and of unrelated books

## The problem

Link places an ebook-side torrent in one of two ways. A **single book** links the files of its best format into one
folder. A **collection** (`split_collections`, `CollectionSplitter`) is split into its books, and each book is placed
as a download of its own would be, in its own best format.

A **multi collection** is one torrent that holds several collections: the issues and story arcs of many series, as in a
reading order across a publisher's titles, or a bundle of unrelated books by several authors. Linked as a single book
it becomes one "book" of hundreds of files in one folder, under whichever series and author MAM lists first, and with
only the files of one format when the books come in several.

A multi collection is a collection, and belongs to the collection linker. `CollectionSplitter` already gives each book
its own series from its file name when MAM's series has no range, so a torrent named `<series> <number>` throughout
(`Lantern 012`, `Lantern Corps 003`, `Nightfall v2 #69`) already splits into its many series. Two kinds of naming are
missed:

- **Reading orders.** Every name starts with the book's place in the reading order: `001_Nightfall - Year Zero.cbr`,
  `022 Nightfall 683.cbr`, `04417 Hollow House 11.cbr`, sometimes with a book folder of its own
  (`734_Lantern - The Long Winter/`). The position is read as part of the series (`022 Nightfall`, `04417 Hollow
  House`), so no series gathers enough books for the torrent to be taken for a collection; or, when some books are
  named without a position, it is split with most books in no series, titled `001_Nightfall - Year Zero`.
- **Bundles.** Standalone books, one per file, named `Austen, Jane - Emma.pdf` by authors MAM lists for the torrent.
  With no range and no numbers, the torrent is not taken for a collection.

## The rule

1. **A reading-order position is not part of a book's name.** In a torrent whose MAM series has no range, when at least
   three quarters of the books' names (and at least `MinimumNumberedBooks`) start with a number of one width followed by
   `_`, ` - ` or a space and then a letter, and those numbers differ, that number is the book's reading-order position.
   It is taken off before the name is read for series, number and title. Books sort by their names, so they stay in
   reading order. A collection whose series has a range keeps today's reading of a leading number as the series number
   (`01 - The Colour of Magic`).
2. **A story arc goes into its series without a number.** In a torrent with reading-order positions, a name
   `<series> - <title>` that has no number of its own, and whose series part is the series of at least one other book,
   is a book of that series titled `<title>`, with no number: `Nightfall - Year Zero` is *Year Zero* in *Nightfall*. A
   `_` between series and title counts as ` - ` (`201_Nightfall_Poison Garden`; `525_Nightfall_Broken Worlds - Hidden
   Door` is *Broken Worlds - Hidden Door*). A name without such a series part is a book with no series, titled as it is
   named.
3. **Detection counts series after the position is gone.** A torrent with no range is a collection when, read by rules 1
   and 2, at least `MinimumNumberedBooks` books share one series.
4. **A bundle of unrelated books is a collection too.** A torrent that rules 1-3 don't take for a collection, whose
   books are at least three quarters (and at least `MinimumNumberedBooks`) named `<Last>, <First> - <Title>` or
   `<First> <Last> - <Title>` by an author MAM lists for the torrent, with at least two such authors, is a collection of
   books with no series. Each such book has its own author, the one MAM lists, and its own title; the others keep the
   torrent's authors.
5. **What doesn't change.** MAM's series range still names the series of a collection's books. A torrent that splits
   today into books of the `<series> <number>` kind splits into the same books, series and numbers.

In a series library a book is placed as a single download of it would be: in its series' folder, or in `oneshots_dir`
when it has no series.

## Steps

1. **The reading-order position, in Core.** `CollectionSplitter`: find the positions by rule 1 and take them off before
   the name is read. Unit tests in `CollectionSplitterTests`: positions with `_`, a space and ` - `; a book named without
   a position among positioned ones; a range collection named `01 - <title>` left as it is; names starting with a year
   (`1961, 03 (March)`) not taken for positions.
2. **Story arcs, in Core.** Rule 2, with tests: `<series> - <title>`, `<series>_<title>`, `<series>_<title> - <more>`,
   a name with no series part, and a series part no other book has.
3. **Detection and bundles, in Core.** Rules 3 and 4, with tests: a reading order of `<position> <series> <number>`
   names taken for a collection; a bundle in both name orders, each book with its own author; one book named
   `Smith, John - Title` in two formats not taken for one; several `<author> - <title>` files by authors MAM doesn't
   list not taken for one; one author's books not taken for a bundle.
4. **What splits today stays as it is.** Tests with the naming of collections that split correctly today across many
   series (`<series> <number>`, `<series> v2 #<number>`, folders per series), run before and after: the same books,
   series and numbers.
5. **Finding the torrents linked before.** A Library check finding, *Collection not split*: a linked torrent, not a
   book of a collection and not split, in a library whose `split_collections` is on, with no choice of its own on its
   page, that `CollectionSplitter.Split` splits by its file list from qBittorrent; with the number of books. Its action
   is Relink, like the other findings. A torrent whose page says not to split is left out.
6. **Docs.** `docs/user-guide.md`, *Collections*: a collection can hold several series, or unrelated books; how
   reading-order positions and story arcs are read; a bundle's books get their own authors; the new finding in *Library
   check*. `RELEASES.md` *Upgrading*: run Library check and relink what *Collection not split* lists; tick **Split
   into its books** for a collection whose page says not to split.
