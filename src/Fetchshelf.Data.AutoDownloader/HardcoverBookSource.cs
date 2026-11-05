using Fetchshelf.Clients.Hardcover;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>Reads the books a `[[hardcover_list]]` follows. An interface so the importer can be tested without Hardcover.</summary>
public interface IHardcoverBookSource
{
    Task<IReadOnlyList<HardcoverBook>> GetBooksAsync(HardcoverListEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>Reads a `[[hardcover_list]]`'s books from Hardcover, finding out once which user the token belongs to.</summary>
public sealed class HardcoverBookSource(HardcoverClient client) : IHardcoverBookSource
{
    private long? _userId;

    public async Task<IReadOnlyList<HardcoverBook>> GetBooksAsync(HardcoverListEntry entry, CancellationToken cancellationToken = default)
    {
        _userId ??= (await client.GetMeAsync(cancellationToken)).Id;
        return entry.List is { } slug
            ? await client.GetListBooksAsync(_userId.Value, slug, cancellationToken)
            : await client.GetBooksWithStatusAsync(_userId.Value, ToReadingStatus(entry.Status!.Value), cancellationToken);
    }

    public static HardcoverReadingStatus ToReadingStatus(HardcoverListStatus status) => status switch
    {
        HardcoverListStatus.WantToRead => HardcoverReadingStatus.WantToRead,
        HardcoverListStatus.CurrentlyReading => HardcoverReadingStatus.CurrentlyReading,
        HardcoverListStatus.Read => HardcoverReadingStatus.Read,
        HardcoverListStatus.Paused => HardcoverReadingStatus.Paused,
        HardcoverListStatus.DidNotFinish => HardcoverReadingStatus.DidNotFinish,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
