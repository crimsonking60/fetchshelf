using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Refreshes a stored torrent's metadata from its snatch list row, without looking the torrent up on MAM.
/// The row lacks some fields (see <see cref="MamSnatchlistDetails"/>); those keep their stored values. The rest goes
/// through <see cref="MamTorrentMapper"/>, so the result is cleaned exactly as metadata from a search is.
/// </summary>
public static class MamSnatchlistRowMapper
{
    private const string AsinPrefix = "ASIN:";

    /// <summary>Throws <see cref="UnrecognizedReferenceDataException"/> as <see cref="MamTorrentMapper.Map"/> does.</summary>
    public static TorrentMetadata Map(MamSnatchlistRow row, MamSnatchlistDetails details, TorrentMetadata stored) =>
        MamTorrentMapper.Map(new MamTorrentRecord
        {
            MamId = row.MamId,
            Title = row.Title,
            Authors = details.Authors,
            Narrators = details.Narrators,
            Series = details.Series,
            LegacyCategoryId = details.LegacyCategoryId,
            GenreCategoryIds = details.GenreCategoryIds,
            FlagsRaw = details.FlagsRaw,
            FileTypesRaw = string.Join(' ', details.FileTypes),
            SizeRaw = details.SizeRaw,
            TagsRaw = details.TagsRaw,
            UploaderId = details.UploaderId,
            UploaderName = details.UploaderName,
            Seeders = row.Seeders,
            Leechers = row.Leechers,
            TimesCompleted = details.TimesCompleted,
            IsFree = details.IsFree,
            IsPersonalFreeleech = details.IsPersonalFreeleech,
            IsFreeleechVip = false,
            IsVip = details.IsVip,
            VipExpireUnixSeconds = details.VipExpireUnixSeconds,
            DownloadHash = row.DownloadHash,

            // Not in the row: keep what is stored.
            MediaTypeId = (int)stored.MediaType,
            OldMainCategoryId = 0,
            FictionCategoryId = stored.FictionCategory is { } fiction ? (int)fiction : 0,
            LanguageId = (int)stored.Language,
            FileCount = stored.FileCount,
            UploadedAt = stored.UploadTime,
            Description = stored.Description,
            Isbn = stored.Isbn ?? (stored.Asin is { } asin ? AsinPrefix + asin : null),
        });
}
