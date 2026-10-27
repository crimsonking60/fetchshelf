namespace Fetchshelf.Core.Metadata;

/// <summary>
/// Rejects incoming MaM metadata whose media type, category, language, size, or date cannot be
/// recognized, per Fetchshelf-spec.md §6.4 ("rejected with a descriptive error ... logged/shown rather than
/// silently dropped"). Genre category ids are deliberately not checked: the tracker keeps adding
/// them, and the original implementation keeps unknown ids rather than rejecting the record.
/// </summary>
public static class ReferenceDataValidator
{
    /// <summary>Validates <paramref name="metadata"/>, throwing <see cref="UnrecognizedReferenceDataException"/> on the first unrecognized field.</summary>
    public static void EnsureRecognized(TorrentMetadata metadata)
    {
        if (!Enum.IsDefined(metadata.MediaType))
        {
            throw new UnrecognizedReferenceDataException("media type", metadata.MediaType);
        }

        if (!Enum.IsDefined(metadata.LegacyCategory))
        {
            throw new UnrecognizedReferenceDataException("category", metadata.LegacyCategory);
        }

        if (!Enum.IsDefined(metadata.Language))
        {
            throw new UnrecognizedReferenceDataException("language", metadata.Language);
        }

        if (metadata.TotalSizeBytes < 0)
        {
            throw new UnrecognizedReferenceDataException("size", metadata.TotalSizeBytes);
        }

        if (metadata.UploadTime == default)
        {
            throw new UnrecognizedReferenceDataException("date", metadata.UploadTime);
        }
    }
}
