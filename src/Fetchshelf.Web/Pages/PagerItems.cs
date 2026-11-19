namespace Fetchshelf.Web.Pages;

/// <summary>One place in a pager: a page's number, or a gap ("…") where pages are left out.</summary>
public sealed record PagerItem(int? Page)
{
    public static PagerItem Gap { get; } = new((int?)null);

    public bool IsGap => Page is null;
}

/// <summary>Which page numbers a pager shows: the first and last page, the current one and two on each side, with a gap where pages are left out.</summary>
public static class PagerItems
{
    /// <summary>How many pages are shown without any gap; beyond this, pages are left out.</summary>
    public const int AllShownUpTo = 7;

    /// <summary>
    /// The items for page <paramref name="current"/> of <paramref name="total"/> (the current page is clamped into range).
    /// Near either end five pages in a row are shown, so the pager is about as wide on every page, and a gap that would
    /// hide a single page shows that page instead.
    /// </summary>
    public static IReadOnlyList<PagerItem> For(int current, int total)
    {
        if (total <= 1)
        {
            return total == 1 ? [new PagerItem(1)] : [];
        }

        current = Math.Clamp(current, 1, total);
        var pages = new SortedSet<int>();
        if (total <= AllShownUpTo)
        {
            pages.UnionWith(Enumerable.Range(1, total));
        }
        else
        {
            var first = current <= 3 ? 1 : current >= total - 2 ? total - 4 : current - 2;
            pages.UnionWith(Enumerable.Range(first, 5));
            pages.Add(1);
            pages.Add(total);
        }

        List<PagerItem> items = [];
        var previous = 0;
        foreach (var page in pages)
        {
            if (page - previous == 2)
            {
                items.Add(new PagerItem(previous + 1));
            }
            else if (page - previous > 2)
            {
                items.Add(PagerItem.Gap);
            }

            items.Add(new PagerItem(page));
            previous = page;
        }

        return items;
    }
}
