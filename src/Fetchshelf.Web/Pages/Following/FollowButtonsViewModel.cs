using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Fetchshelf.Web.Pages.Torrents;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// Follow buttons for some authors or series, as on a torrent's page: a button for each one not followed yet, and a link
/// to the Following page for each one that is. Names without a key can't be followed and are left out.
/// </summary>
/// <param name="Followed">The modes of everything followed, by kind and key (<see cref="LoadFollowedAsync"/>).</param>
/// <param name="ReturnUrl">Where following one comes back to.</param>
public sealed record FollowButtonsViewModel(
    FollowKind Kind, IReadOnlyList<NameLink> Names, IReadOnlyDictionary<(FollowKind, string), FollowMode> Followed, string ReturnUrl)
{
    public IEnumerable<(NameLink Name, FollowMode? Mode)> Buttons =>
        Names.Where(n => n.Key.Length > 0).DistinctBy(n => n.Key)
            .Select(n => (n, Followed.TryGetValue((Kind, n.Key), out var mode) ? mode : (FollowMode?)null));

    public static async Task<IReadOnlyDictionary<(FollowKind, string), FollowMode>> LoadFollowedAsync(
        FetchshelfDbContext dbContext, CancellationToken cancellationToken = default) =>
        (await dbContext.Follows.AsNoTracking().Select(f => new { f.Kind, f.NameKey, f.Mode }).ToListAsync(cancellationToken))
            .ToDictionary(f => (f.Kind, f.NameKey), f => f.Mode);
}
