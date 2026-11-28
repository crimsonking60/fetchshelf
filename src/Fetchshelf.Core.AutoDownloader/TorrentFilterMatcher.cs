using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// Evaluates the shared torrent filter against a candidate. A torrent matches only
/// if every field the filter sets is satisfied; unset fields impose no constraint.
/// </summary>
public static class TorrentFilterMatcher
{
    public static bool Matches(TorrentFilter filter, TorrentFilterCandidate candidate) => Mismatch(filter, candidate) is null;

    /// <summary>
    /// The first of the filter's fields the torrent fails, in words, such as "language German" or "0 seeders"; null when
    /// it passes them all. For logs that say why a torrent was turned down.
    /// </summary>
    public static string? Mismatch(TorrentFilter filter, TorrentFilterCandidate candidate)
    {
        var meta = candidate.Metadata;

        if (filter.MediaTypes is { Count: > 0 } && !filter.MediaTypes.Contains(meta.MediaType))
        {
            return $"media type {meta.MediaType}";
        }

        if (!MatchesCategories(filter.Categories, meta.MediaType, meta.LegacyCategory))
        {
            return $"category {meta.LegacyCategory}";
        }

        if (filter.Languages is { Count: > 0 } && !filter.Languages.Contains(meta.Language))
        {
            return $"language {meta.Language}";
        }

        if (filter.Flags is not null)
        {
            foreach (var (flag, required) in filter.Flags)
            {
                var present = meta.Flags.HasFlag(flag);
                if (present != required)
                {
                    return $"flag {flag}";
                }
            }
        }

        if (filter.MinSizeBytes is { } minSize && meta.TotalSizeBytes < minSize)
        {
            return "smaller than min_size";
        }

        if (filter.MaxSizeBytes is { } maxSize && meta.TotalSizeBytes > maxSize)
        {
            return "larger than max_size";
        }

        if (filter.ExcludeUploader is { Count: > 0 }
            && filter.ExcludeUploader.Contains(candidate.UploaderName, StringComparer.OrdinalIgnoreCase))
        {
            return $"uploader {candidate.UploaderName}";
        }

        var uploadDate = DateOnly.FromDateTime(meta.UploadTime.UtcDateTime);

        if (filter.UploadedAfter is { } after && uploadDate < after)
        {
            return "uploaded before uploaded_after";
        }

        if (filter.UploadedBefore is { } before && uploadDate > before)
        {
            return "uploaded after uploaded_before";
        }

        if (filter.MinSeeders is { } minSeeders && candidate.Seeders < minSeeders)
        {
            return $"{candidate.Seeders} seeders";
        }

        if (filter.MaxSeeders is { } maxSeeders && candidate.Seeders > maxSeeders)
        {
            return $"{candidate.Seeders} seeders";
        }

        if (filter.MinLeechers is { } minLeechers && candidate.Leechers < minLeechers)
        {
            return $"{candidate.Leechers} leechers";
        }

        if (filter.MaxLeechers is { } maxLeechers && candidate.Leechers > maxLeechers)
        {
            return $"{candidate.Leechers} leechers";
        }

        if (filter.MinSnatched is { } minSnatched && candidate.TimesCompleted < minSnatched)
        {
            return $"snatched {candidate.TimesCompleted} times";
        }

        if (filter.MaxSnatched is { } maxSnatched && candidate.TimesCompleted > maxSnatched)
        {
            return $"snatched {candidate.TimesCompleted} times";
        }

        if (filter.MaxAuthors is { } maxAuthors && meta.Authors.Count > maxAuthors)
        {
            return $"{meta.Authors.Count} authors";
        }

        return null;
    }

    private static bool MatchesCategories(
        CategoryFilter categories, MediaType mediaType, LegacyCategory category)
    {
        var mainType = FilterMainTypeMapper.FromMediaType(mediaType);
        var group = mainType switch
        {
            FilterMainType.Audio => categories.Audio,
            FilterMainType.Ebook => categories.Ebook,
            FilterMainType.Musicology => categories.Musicology,
            FilterMainType.Radio => categories.Radio,
            _ => throw new ArgumentOutOfRangeException(nameof(mainType)),
        };

        return group.Allowed is null || group.Allowed.Contains(category);
    }
}
