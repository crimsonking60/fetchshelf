using Fetchshelf.Core.Catalog;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Web.Catalog;

namespace Fetchshelf.Web.Pages.Books;

/// <summary>One book in the Books page's grid.</summary>
/// <param name="Authors">Every author, in the book's order; the card shows the first <see cref="AuthorsShown"/>.</param>
/// <param name="SeriesLine">The book's first series and number, or with <c>series</c> given, its number in that series.</param>
/// <param name="Formats">The formats it has, such as "M4B" and "PDF".</param>
/// <param name="CoverHref">Where its cover is, or null when it can't have one Fetchshelf reads.</param>
/// <param name="Initial">The title's first letter, shown in place of a missing cover.</param>
public sealed record BookCardViewModel(
    string Id, string Title, IReadOnlyList<string> Authors, string? SeriesLine, IReadOnlyList<string> Formats, string? CoverHref, string Initial)
{
    /// <summary>How many authors a card shows; a book with more shows these and "... more", which opens the book with all of them.</summary>
    public const int AuthorsShown = 3;

    /// <summary>The authors the card shows, joined.</summary>
    public string ShownAuthors => string.Join(", ", Authors.Take(AuthorsShown));

    /// <summary>Whether the book has authors the card leaves out.</summary>
    public bool HasMoreAuthors => Authors.Count > AuthorsShown;

    public static BookCardViewModel From(CatalogBook book, string? series = null)
    {
        var shownSeries = series is null ? book.Series.FirstOrDefault() : book.Series.FirstOrDefault(s => s.Name == series);
        return new BookCardViewModel(
            book.Id,
            book.Title,
            book.Authors,
            SeriesText(shownSeries),
            FormatsOf(book),
            book.MayHaveCover ? BookEndpoints.CoverHref(book) : null,
            book.Title.Length > 0 ? book.Title[..1].ToUpperInvariant() : "?");
    }

    /// <summary>"Name #2", or the name alone without a number.</summary>
    public static string? SeriesText(CatalogSeries? series) =>
        series is null ? null : string.IsNullOrEmpty(series.Entry) ? series.Name : $"{series.Name} #{series.Entry}";

    public static IReadOnlyList<string> FormatsOf(CatalogBook book) =>
        book.AudioFiles.Select(file => file.Extension).Concat(book.EbookFiles.Select(file => file.Extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(extension => extension.ToUpperInvariant())
            .ToList();

    /// <summary>A media type as the page names it.</summary>
    public static string MediaTypeLabel(MediaType mediaType) => mediaType switch
    {
        MediaType.Audiobook => "Audiobook",
        MediaType.Ebook => "Ebook",
        MediaType.Musicology => "Musicology",
        MediaType.Radio => "Radio",
        MediaType.Manga => "Manga",
        MediaType.ComicBook => "Comic book",
        MediaType.PeriodicalEbook => "Periodical (ebook)",
        MediaType.PeriodicalAudiobook => "Periodical (audiobook)",
        _ => mediaType.ToString(),
    };
}
