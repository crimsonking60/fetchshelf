using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>
/// What a library asks of a book's genres and main category (`allow_genres`, `deny_genres`, `fiction`). The two are
/// separate dimensions: a <i>Historical</i> book can be fiction or nonfiction. Every condition that is set must hold.
/// A book without genres matches no <see cref="AllowGenres"/> and is never refused by <see cref="DenyGenres"/>; a book
/// without a main category matches no <see cref="Fiction"/>.
/// </summary>
/// <param name="AllowGenres">If not empty, the book must have at least one of these genres.</param>
/// <param name="DenyGenres">A book with any of these genres is not taken.</param>
/// <param name="Fiction">If set, the book's main category must be this one.</param>
public sealed record GenreCondition(IReadOnlySet<GenreCategory> AllowGenres, IReadOnlySet<GenreCategory> DenyGenres, FictionCategory? Fiction)
{
    /// <summary>No condition: takes every book.</summary>
    public static GenreCondition None { get; } = new(new HashSet<GenreCategory>(), new HashSet<GenreCategory>(), null);

    /// <summary>Whether the condition asks nothing, so a library with it takes every book its category and tags give it.</summary>
    public bool IsEmpty => AllowGenres.Count == 0 && DenyGenres.Count == 0 && Fiction is null;

    public bool Matches(TorrentMetadata metadata)
    {
        var genres = metadata.GenreCategories ?? new HashSet<GenreCategory>();
        if (AllowGenres.Count > 0 && !genres.Any(AllowGenres.Contains))
        {
            return false;
        }

        if (genres.Any(DenyGenres.Contains))
        {
            return false;
        }

        return Fiction is not { } fiction || metadata.FictionCategory == fiction;
    }
}
