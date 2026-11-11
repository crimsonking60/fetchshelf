using System.Globalization;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Series;

/// <summary>One series on the Series page: what the library has of it, what is missing, and whether it is followed.</summary>
public sealed record SeriesRowViewModel(
    string Key, string Name, IReadOnlyList<string> Authors, string Owned, string Missing, IReadOnlyList<string> MediaTypes, int Books,
    FollowEntity? Follow)
{
    public bool HasMissing => Missing.Length > 0;

    /// <summary>The id the row has on the page, which its buttons swap.</summary>
    public string RowId => "series-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(Key)))[..12].ToLowerInvariant();

    public static SeriesRowViewModel From(SeriesSummary summary, IReadOnlyList<string> authors, FollowEntity? follow)
    {
        var numbered = summary.Entries.Where(e => e.FirstNumber is not null).Select(e => (e.FirstNumber!.Value, e.LastNumber ?? e.FirstNumber!.Value)).ToList();
        var covered = SeriesGaps.Covered(numbered);
        // Entries that are no whole book (a novella at 1.5) are listed as they are written.
        var others = summary.Entries
            .Where(e => e.Display is not null && (e.FirstNumber is not { } first || first != Math.Floor(first) || first > SeriesGaps.MaxBookNumber))
            .Select(e => e.Display!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(d => double.TryParse(d, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.MaxValue);
        var owned = string.Join(", ", new[] { SeriesGaps.Describe(covered) }.Concat(others).Where(part => part.Length > 0));
        return new SeriesRowViewModel(
            summary.NameKey, summary.Name, authors, owned, SeriesGaps.Describe(SeriesGaps.Missing(numbered)), summary.MediaTypes, summary.Books,
            follow);
    }
}
