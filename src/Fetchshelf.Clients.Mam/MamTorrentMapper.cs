using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Maps a raw <see cref="MamTorrentRecord"/> to a cleaned <see cref="TorrentMetadata"/> using
/// Fetchshelf.Core's cleaning pieces (<see cref="MamMetadataCleaner"/>, <see cref="EditionExtractor"/>,
/// <see cref="ReferenceDataValidator"/>).
/// </summary>
public static class MamTorrentMapper
{
    private const string AsinPrefix = "ASIN:";

    /// <summary>
    /// Maps and cleans <paramref name="record"/>. Throws <see cref="UnrecognizedReferenceDataException"/>
    /// if any reference-data field is unrecognized, so the record is rejected with a descriptive error
    /// rather than silently dropped. Callers are expected to catch and log it rather than let it crash the
    /// caller's own flow.
    /// </summary>
    public static TorrentMetadata Map(MamTorrentRecord record)
    {
        var mediaType = (MediaType)EffectiveMediaTypeId(record);
        var legacyCategory = (LegacyCategory)record.LegacyCategoryId;
        mediaType = MamMetadataCleaner.ReclassifyMediaType(mediaType, legacyCategory);

        var authors = record.Authors.OrderBy(kv => kv.Key)
            .Select(kv => MamMetadataCleaner.NormalizeNameCasing(MamMetadataCleaner.DecodeHtmlEntities(kv.Value)))
            .ToList();
        var narrators = record.Narrators.OrderBy(kv => kv.Key)
            .Select(kv => MamMetadataCleaner.NormalizeNameCasing(MamMetadataCleaner.DecodeHtmlEntities(kv.Value)))
            .ToList();
        var series = record.Series
            .Select(info =>
            {
                var name = MamMetadataCleaner.StripSeriesSuffix(MamMetadataCleaner.DecodeHtmlEntities(info.Name));
                return SeriesParser.Parse(name, info.Number);
            })
            .ToList();

        var title = MamMetadataCleaner.DecodeHtmlEntities(record.Title);
        Edition? edition = null;

        var titleMatch = EditionExtractor.TryExtract(title);
        if (titleMatch is not null)
        {
            edition = new Edition(titleMatch.Label, titleMatch.Number);
            title = EditionExtractor.RemoveFromTitle(title, titleMatch);
        }
        else
        {
            var tagsMatch = EditionExtractor.TryExtract(record.TagsRaw);
            if (tagsMatch is not null)
            {
                edition = new Edition(tagsMatch.Label, tagsMatch.Number);
            }
        }

        // An upload without an author can't be linked; some name it in the title instead. Periodicals are left
        // alone, as StripAuthorReference leaves their title alone: "by" there usually names a publisher.
        var isPeriodical = mediaType is MediaType.PeriodicalEbook or MediaType.PeriodicalAudiobook;
        if (authors.Count == 0 && !isPeriodical && MamMetadataCleaner.SplitAuthorFromTitle(title) is { } split)
        {
            title = split.Title;
            authors = [MamMetadataCleaner.NormalizeNameCasing(split.Author)];
        }

        title = MamMetadataCleaner.StripAuthorReference(title, authors, mediaType);
        title = MamMetadataCleaner.RemoveTitleNoise(title);

        var isbn = record.Isbn;
        string? asin = null;
        if (isbn is not null && isbn.StartsWith(AsinPrefix, StringComparison.OrdinalIgnoreCase))
        {
            asin = isbn[AsinPrefix.Length..];
            isbn = null;
        }

        var metadata = new TorrentMetadata
        {
            MamId = record.MamId,
            VipStatus = BuildVipStatus(record),
            MediaType = mediaType,
            FictionCategory = Enum.IsDefined((FictionCategory)record.FictionCategoryId) ? (FictionCategory)record.FictionCategoryId : null,
            LegacyCategory = legacyCategory,
            GenreCategories = record.GenreCategoryIds.Select(id => (GenreCategory)id).ToHashSet(),
            Language = (Language)record.LanguageId,
            Flags = (BookFlags)record.FlagsRaw,
            FileTypes = record.FileTypesRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            FileCount = record.FileCount,
            TotalSizeBytes = MamSizeParser.ParseBytes(record.SizeRaw),
            Title = title,
            Edition = edition,
            Authors = authors,
            Narrators = narrators,
            Series = series,
            MetadataSource = MetadataSource.Mam,
            UploadTime = record.UploadedAt,
            Description = record.Description,
            Isbn = isbn,
            Asin = asin,
        };

        ReferenceDataValidator.EnsureRecognized(metadata);
        return metadata;
    }

    /// <summary>
    /// The record's MAM media type id. Older records carry only the old main category (13-16), which maps to media
    /// types 1-4; 0 when neither is known.
    /// </summary>
    public static int EffectiveMediaTypeId(MamTorrentRecord record) =>
        record.MediaTypeId != 0 ? record.MediaTypeId : OldMainCategoryToMediaTypeId(record.OldMainCategoryId);

    private static int OldMainCategoryToMediaTypeId(int oldMainCategoryId) => oldMainCategoryId switch
    {
        13 => 1, // Audiobook
        14 => 2, // Ebook
        15 => 3, // Musicology
        16 => 4, // Radio
        _ => 0,
    };

    private static VipStatus BuildVipStatus(MamTorrentRecord record)
    {
        if (!record.IsVip)
        {
            return VipStatus.NotVip;
        }

        if (record.VipExpireUnixSeconds == 0)
        {
            return VipStatus.Permanent;
        }

        var expiresOn = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(record.VipExpireUnixSeconds).UtcDateTime);
        return VipStatus.Temporary(expiresOn);
    }
}
