using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Fetchshelf.Web.Pages.Torrents;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// Follow buttons for some authors or series, as on a torrent's page: a button for each one not followed yet, and a link
/// to the Following page for each one that is. Names without a key can't be followed and are left out.
/// </summary>
/// <param name="Followed">Everything followed, by kind and key (<see cref="LoadFollowedAsync"/>).</param>
/// <param name="ReturnUrl">Where following one comes back to.</param>
public sealed record FollowButtonsViewModel(
    FollowKind Kind, IReadOnlyList<NameLink> Names, IReadOnlyDictionary<(FollowKind, string), FollowState> Followed, string ReturnUrl)
{
    public IEnumerable<(NameLink Name, FollowState? State)> Buttons =>
        Names.Where(n => n.Key.Length > 0).DistinctBy(n => n.Key)
            .Select(n => (n, Followed.TryGetValue((Kind, n.Key), out var state) ? state : null));

    public static async Task<IReadOnlyDictionary<(FollowKind, string), FollowState>> LoadFollowedAsync(
        FetchshelfDbContext dbContext, CancellationToken cancellationToken = default) =>
        (await dbContext.Follows.AsNoTracking().Select(f => new { f.Id, f.Kind, f.NameKey, f.Mode }).ToListAsync(cancellationToken))
            .ToDictionary(f => (f.Kind, f.NameKey), f => new FollowState(f.Id, f.Mode));
}

/// <summary>A followed or ignored series or author: its page's id, and its mode.</summary>
public sealed record FollowState(int Id, FollowMode Mode);
