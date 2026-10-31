using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// Evaluates the shared torrent filter against a candidate. A torrent matches only
/// if every field the filter sets is satisfied; unset fields impose no constraint.
/// </summary>
public static class TorrentFilterMatcher
{
    public static bool Matches(TorrentFilter filter, TorrentFilterCandidate candidate)
    {
        var meta = candidate.Metadata;

        if (filter.MediaTypes is { Count: > 0 } && !filter.MediaTypes.Contains(meta.MediaType))
        {
            return false;
        }

        if (!MatchesCategories(filter.Categories, meta.MediaType, meta.LegacyCategory))
        {
            return false;
        }

        if (filter.Languages is { Count: > 0 } && !filter.Languages.Contains(meta.Language))
        {
            return false;
        }

        if (filter.Flags is not null)
        {
            foreach (var (flag, required) in filter.Flags)
            {
                var present = meta.Flags.HasFlag(flag);
                if (present != required)
                {
                    return false;
                }
            }
        }

        if (filter.MinSizeBytes is { } minSize && meta.TotalSizeBytes < minSize)
        {
            return false;
        }

        if (filter.MaxSizeBytes is { } maxSize && meta.TotalSizeBytes > maxSize)
        {
            return false;
        }

        if (filter.ExcludeUploader is { Count: > 0 }
            && filter.ExcludeUploader.Contains(candidate.UploaderName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var uploadDate = DateOnly.FromDateTime(meta.UploadTime.UtcDateTime);

        if (filter.UploadedAfter is { } after && uploadDate < after)
        {
            return false;
        }

        if (filter.UploadedBefore is { } before && uploadDate > before)
        {
            return false;
        }

        if (filter.MinSeeders is { } minSeeders && candidate.Seeders < minSeeders)
        {
            return false;
        }

        if (filter.MaxSeeders is { } maxSeeders && candidate.Seeders > maxSeeders)
        {
            return false;
        }

        if (filter.MinLeechers is { } minLeechers && candidate.Leechers < minLeechers)
        {
            return false;
        }

        if (filter.MaxLeechers is { } maxLeechers && candidate.Leechers > maxLeechers)
        {
            return false;
        }

        if (filter.MinSnatched is { } minSnatched && candidate.TimesCompleted < minSnatched)
        {
            return false;
        }

        if (filter.MaxSnatched is { } maxSnatched && candidate.TimesCompleted > maxSnatched)
        {
            return false;
        }

        if (filter.MaxAuthors is { } maxAuthors && meta.Authors.Count > maxAuthors)
        {
            return false;
        }

        return true;
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
