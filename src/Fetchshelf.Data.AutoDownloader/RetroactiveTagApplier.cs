using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What applying a tag rule would do to one torrent in qBittorrent.</summary>
/// <param name="TorrentId">The torrent's info hash.</param>
/// <param name="Title">Its title.</param>
/// <param name="IsLinked">Whether it is in a library now, where a new category can move it to another library.</param>
/// <param name="CurrentCategory">Its category in qBittorrent now; empty for none.</param>
/// <param name="NewCategory">The category the rule sets, when that is another one; null when the category stays.</param>
/// <param name="TagsAdded">The rule's tags it doesn't have yet.</param>
public sealed record TagRuleChange(
    string TorrentId, string Title, bool IsLinked, string CurrentCategory, string? NewCategory, IReadOnlyList<string> TagsAdded);

/// <summary>What applying a tag rule to the stored torrents would do, without doing it.</summary>
/// <param name="Changes">The torrents it would change, by title.</param>
/// <param name="Unchanged">Matching torrents that already have the rule's category and tags.</param>
/// <param name="NotInClient">Matching torrents no configured qBittorrent has, which it leaves alone.</param>
public sealed record TagRulePreview(IReadOnlyList<TagRuleChange> Changes, int Unchanged, int NotInClient);

/// <summary>
/// Applies one `[[tag]]` rule to already-stored library torrents on demand, after <see cref="PreviewAsync"/> has shown
/// what it would change. Distinct from grab-time tag resolution (<see cref="TagRuleResolver"/>), which runs inside
/// <see cref="SelectionRunner"/> instead.
/// </summary>
public sealed class RetroactiveTagApplier(
    FetchshelfDbContext dbContext,
    MamClient mamClient,
    MamAccessGate accessGate,
    IReadOnlyList<QBittorrentClient> qbittorrentClients)
{
    /// <summary>
    /// What <see cref="ApplyAsync"/> would change, read from qBittorrent and changing nothing: each matching torrent whose
    /// category or tags the rule would change, with what it has now. A torrent that already has the rule's category and
    /// tags is only counted.
    /// </summary>
    public async Task<TagRulePreview> PreviewAsync(TagRule rule, CancellationToken cancellationToken = default)
    {
        var (changes, unchanged, notInClient) = await ChangesAsync(rule, cancellationToken);
        return new TagRulePreview(changes.Select(c => c.Change).ToList(), unchanged, notInClient);
    }

    /// <summary>
    /// Sets the rule's category and adds its tags in qBittorrent, on the torrents <see cref="PreviewAsync"/> lists as
    /// changing, as they are now. Returns how many were changed.
    /// </summary>
    public async Task<int> ApplyAsync(TagRule rule, CancellationToken cancellationToken = default)
    {
        var (changes, _, _) = await ChangesAsync(rule, cancellationToken);
        foreach (var (change, client) in changes)
        {
            if (change.NewCategory is { } category)
            {
                await client.SetCategoryAsync(change.TorrentId, category, cancellationToken);
            }

            if (change.TagsAdded.Count > 0)
            {
                await client.AddTagsAsync(change.TorrentId, change.TagsAdded, cancellationToken);
            }
        }

        return changes.Count;
    }

    /// <summary>The changes the rule makes, each with the qBittorrent that has the torrent, by title; and the matches it leaves alone.</summary>
    private async Task<(List<(TagRuleChange Change, QBittorrentClient Client)> Changes, int Unchanged, int NotInClient)> ChangesAsync(
        TagRule rule, CancellationToken cancellationToken)
    {
        // Each client's whole list once, rather than one request per torrent. The first client with a torrent owns it.
        var inClients = new Dictionary<string, (QBittorrentTorrent Torrent, QBittorrentClient Client)>(StringComparer.OrdinalIgnoreCase);
        foreach (var client in qbittorrentClients)
        {
            foreach (var torrent in await client.ListTorrentsAsync(cancellationToken))
            {
                inClients.TryAdd(torrent.Hash, (torrent, client));
            }
        }

        var changes = new List<(TagRuleChange, QBittorrentClient)>();
        var (unchanged, notInClient) = (0, 0);
        await foreach (var torrent in MatchingAsync(rule, cancellationToken))
        {
            if (!inClients.TryGetValue(torrent.Id, out var owned))
            {
                notInClient++;
                continue;
            }

            var current = owned.Torrent;
            var newCategory = rule.Category is { } category && !string.Equals(category, current.Category, StringComparison.Ordinal) ? category : null;
            var tagsAdded = rule.Tags.Where(tag => !current.TagList.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();
            if (newCategory is null && tagsAdded.Count == 0)
            {
                unchanged++;
                continue;
            }

            changes.Add((new TagRuleChange(torrent.Id, torrent.Title, torrent.LibraryPath is not null, current.Category, newCategory, tagsAdded), owned.Client));
        }

        return (changes.OrderBy(c => c.Item1.Title, StringComparer.OrdinalIgnoreCase).ToList(), unchanged, notInClient);
    }

    /// <summary>The stored torrents the rule's filter matches, metadata-only records left out: they aren't in qBittorrent.</summary>
    private async IAsyncEnumerable<TorrentEntity> MatchingAsync(
        TagRule rule, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var torrents = await dbContext.Torrents.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var torrent in torrents)
        {
            if (MetadataOnlyTorrentId.IsMetadataOnly(torrent.Id))
            {
                continue;
            }

            var metadata = TorrentMetadataSerializer.Deserialize(torrent.MetadataJson);
            var candidate = await BuildCandidateAsync(rule.Filter, metadata, cancellationToken);
            if (TorrentFilterMatcher.Matches(rule.Filter, candidate))
            {
                yield return torrent;
            }
        }
    }

    private async Task<TorrentFilterCandidate> BuildCandidateAsync(
        TorrentFilter filter, TorrentMetadata metadata, CancellationToken cancellationToken)
    {
        if (!filter.NeedsLiveData)
        {
            return new TorrentFilterCandidate
            {
                Metadata = metadata,
                UploaderName = string.Empty,
                Seeders = 0,
                Leechers = 0,
                TimesCompleted = 0,
            };
        }

        MamSearchResult result;
        using (await accessGate.AcquireAsync("Tagging a torrent", cancellationToken))
        {
            result = await mamClient.SearchAsync(new MamSearchQuery { Id = metadata.MamId }, cancellationToken);
        }

        var record = result.Data.FirstOrDefault();

        return new TorrentFilterCandidate
        {
            Metadata = metadata,
            UploaderName = record?.UploaderName ?? string.Empty,
            Seeders = record?.Seeders ?? 0,
            Leechers = record?.Leechers ?? 0,
            TimesCompleted = record?.TimesCompleted ?? 0,
        };
    }
}
