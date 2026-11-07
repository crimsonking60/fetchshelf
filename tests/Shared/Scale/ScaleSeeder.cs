using System.Text.Json;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Tests.Scale;

/// <summary>A seeded library: the database rows, and the torrent list qBittorrent reports for it.</summary>
internal sealed record ScaleData(
    IReadOnlyList<TorrentEntity> Torrents,
    IReadOnlyList<EventEntity> Events,
    IReadOnlyList<ErroredTorrentEntity> Errors,
    IReadOnlyList<QBittorrentTorrent> ClientTorrents)
{
    /// <summary>Torrents linked into a library, i.e. not retired in favour of a better copy.</summary>
    public int LinkedCount => Torrents.Count(t => t.LibraryPath is not null);

    /// <summary>Finished torrents in the client that Fetchshelf failed to look up on MAM.</summary>
    public int FailedLookupCount => Errors.Count;
}

/// <summary>
/// Builds a large, realistic library in steady state, for timing the jobs and pages that grow with library size:
/// <list type="bullet">
/// <item>audiobooks and ebooks, mostly English, with shared authors, narrators and series;</item>
/// <item>every 20th book has an older, lesser copy already retired by the Clean job;</item>
/// <item>a Grabbed and a Linked event for most books, an Updated event for some, a Cleaned event per retired copy;</item>
/// <item>1% extra finished torrents in a library's category whose MAM lookup failed, recorded as linker errors;</item>
/// <item>10% extra torrents in a category no library takes.</item>
/// </list>
/// The same count always gives the same data.
/// </summary>
internal static class ScaleSeeder
{
    public const string AudiobookCategory = "audiobooks";
    public const string EbookCategory = "ebooks";
    public const string AudiobookLibraryName = "Audiobooks";
    public const string EbookLibraryName = "Ebooks";

    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Language[] OtherLanguages = [Language.German, Language.French, Language.Spanish, Language.Dutch];
    private static readonly LegacyCategory[] AudiobookCategories =
        [LegacyCategory.AudiobookFantasy, LegacyCategory.AudiobookScienceFiction, LegacyCategory.AudiobookHistory];
    private static readonly LegacyCategory[] EbookCategories =
        [LegacyCategory.EbookFantasy, LegacyCategory.EbookScienceFiction, LegacyCategory.EbookGeneralFiction, LegacyCategory.EbookHistory];

    /// <param name="torrentCount">Torrent rows to create, retired copies included.</param>
    /// <param name="downloadsDir">Save path of every torrent in the client.</param>
    /// <param name="audiobookLibraryDir">Library folder for the `audiobooks` category.</param>
    /// <param name="ebookLibraryDir">Library folder for the `ebooks` category.</param>
    public static ScaleData Build(int torrentCount, string downloadsDir, string audiobookLibraryDir, string ebookLibraryDir)
    {
        var random = new Random(torrentCount);
        var torrents = new List<TorrentEntity>(torrentCount);
        var events = new List<EventEntity>(torrentCount * 3);
        var clientTorrents = new List<QBittorrentTorrent>(torrentCount + (torrentCount / 5));
        var authorCount = Math.Max(1, torrentCount / 4);
        var seriesCount = Math.Max(1, torrentCount / 10);

        for (var book = 0; torrents.Count < torrentCount; book++)
        {
            var isAudiobook = random.NextDouble() < 0.6;
            var series = random.NextDouble() < 0.4
                ? new List<Series>
                {
                    new()
                    {
                        Name = $"Series {random.Next(seriesCount):D5}",
                        Entries = [SeriesEntry.ForNumber(random.Next(1, 12), "1")],
                    },
                }
                : [];
            var metadata = new TorrentMetadata
            {
                MamId = 100_000 + book,
                VipStatus = VipStatus.NotVip,
                MediaType = isAudiobook ? MediaType.Audiobook : MediaType.Ebook,
                LegacyCategory = isAudiobook
                    ? AudiobookCategories[random.Next(AudiobookCategories.Length)]
                    : EbookCategories[random.Next(EbookCategories.Length)],
                Language = random.NextDouble() < 0.85 ? Language.English : OtherLanguages[random.Next(OtherLanguages.Length)],
                Title = $"The {Word(random)} of {Word(random)} {book:D6}",
                Authors = [$"Author {random.Next(authorCount):D5}"],
                Narrators = isAudiobook ? [$"Narrator {random.Next(500):D3}"] : [],
                Series = series,
                MetadataSource = MetadataSource.Mam,
                UploadTime = Start.AddMinutes(book),
                Description = string.Join(' ', Enumerable.Range(0, 60).Select(_ => Word(random))),
            };

            var libraryDir = isAudiobook ? audiobookLibraryDir : ebookLibraryDir;
            var category = isAudiobook ? AudiobookCategory : EbookCategory;
            var libraryName = isAudiobook ? AudiobookLibraryName : EbookLibraryName;
            var extension = isAudiobook ? "m4b" : "epub";
            var createdAt = Start.AddMinutes(book * 7);
            var keeper = new TorrentEntity
            {
                Id = Hash(book, copy: 0),
                MamId = metadata.MamId,
                NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
                Title = metadata.Title,
                MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
                CreatedAt = createdAt,
                LibraryPath = FolderNameComputer.Compute(libraryDir, metadata, excludeNarratorInLibraryDir: false, _ => false),
                LinkedFilesJson = JsonSerializer.Serialize(new List<LinkedFileRecord> { new($"{metadata.Title}.{extension}", 400_000_000) }),
                ChosenAudioExtension = isAudiobook ? extension : null,
                ChosenEbookExtension = isAudiobook ? null : extension,
                QBittorrentCategory = category,
                LinkerName = libraryName,
                ClientStatus = "uploading",
            };
            torrents.Add(keeper);
            clientTorrents.Add(ClientTorrent(keeper, downloadsDir, category));
            var grabbed = Event(EventKind.Grabbed, keeper, createdAt.AddMinutes(-30));
            grabbed.GrabberName = "Autograb";
            grabbed.Cost = "Ratio";
            events.Add(grabbed);
            var linked = Event(EventKind.Linked, keeper, createdAt);
            linked.LinkerName = libraryName;
            linked.LibraryPath = keeper.LibraryPath;
            events.Add(linked);

            if (random.NextDouble() < 0.1)
            {
                var updated = Event(EventKind.Updated, keeper, createdAt.AddDays(3));
                updated.FieldChangesJson = """[{"Field":"Description","From":"old","To":"new"}]""";
                events.Add(updated);
            }

            if (book % 20 == 19 && torrents.Count < torrentCount)
            {
                var lesserExtension = isAudiobook ? "mp3" : "pdf";
                var retired = new TorrentEntity
                {
                    Id = Hash(book, copy: 1),
                    MamId = metadata.MamId + 1_000_000,
                    NormalizedTitleKey = keeper.NormalizedTitleKey,
                    Title = keeper.Title,
                    MetadataJson = keeper.MetadataJson,
                    CreatedAt = createdAt.AddDays(-30),
                    ChosenAudioExtension = isAudiobook ? lesserExtension : null,
                    ChosenEbookExtension = isAudiobook ? null : lesserExtension,
                    QBittorrentCategory = "cleaned",
                    LinkerName = libraryName,
                    ClientStatus = "uploading",
                    ReplacedWithTorrentId = keeper.Id,
                    ReplacedAt = createdAt,
                };
                torrents.Add(retired);
                clientTorrents.Add(ClientTorrent(retired, downloadsDir, "cleaned"));
                var cleaned = Event(EventKind.Cleaned, retired, createdAt);
                cleaned.LinkerName = libraryName;
                cleaned.FilesRemovedJson = JsonSerializer.Serialize(new[] { $"{metadata.Title}.{lesserExtension}" });
                events.Add(cleaned);
            }
        }

        var errors = new List<ErroredTorrentEntity>();
        for (var i = 0; i < torrentCount / 100; i++)
        {
            var hash = Hash(10_000_000 + i, copy: 0);
            clientTorrents.Add(new QBittorrentTorrent
            {
                Hash = hash,
                Name = $"Unknown upload {i}",
                SavePath = downloadsDir,
                Category = AudiobookCategory,
                State = "uploading",
                Progress = 1.0,
            });
            errors.Add(new ErroredTorrentEntity
            {
                Stage = ErrorStage.Linker,
                Key = hash,
                Title = $"Unknown upload {i}",
                ErrorText = "MAM has no torrent with this info-hash.",
                OccurredAt = Start.AddDays(i),
            });
        }

        for (var i = 0; i < torrentCount / 10; i++)
        {
            clientTorrents.Add(new QBittorrentTorrent
            {
                Hash = Hash(20_000_000 + i, copy: 0),
                Name = $"linux-{i}.iso",
                SavePath = "/downloads/other",
                Category = "linux-isos",
                State = "uploading",
                Progress = 1.0,
            });
        }

        return new ScaleData(torrents, events, errors, clientTorrents);
    }

    /// <summary>Writes <paramref name="data"/> in one batch and leaves nothing tracked, so a job starts as it would in production.</summary>
    public static async Task SaveAsync(FetchshelfDbContext dbContext, ScaleData data)
    {
        dbContext.Torrents.AddRange(data.Torrents);
        dbContext.Events.AddRange(data.Events);
        dbContext.ErroredTorrents.AddRange(data.Errors);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static QBittorrentTorrent ClientTorrent(TorrentEntity torrent, string downloadsDir, string category) => new()
    {
        Hash = torrent.Id,
        Name = torrent.Title,
        SavePath = downloadsDir,
        Category = category,
        State = "uploading",
        Progress = 1.0,
    };

    private static EventEntity Event(EventKind kind, TorrentEntity torrent, DateTimeOffset occurredAt) => new()
    {
        OccurredAt = occurredAt,
        TorrentId = torrent.Id,
        MamId = torrent.MamId,
        Kind = kind,
    };

    private static string Hash(int book, int copy) => $"{copy:x8}{book:x32}";

    private static readonly string[] Words =
    [
        "Kings", "Shadow", "River", "Glass", "Ember", "Winter", "Crown", "Storm", "Garden", "Iron", "Silver", "Night",
        "Ashes", "Tide", "Hollow", "Stone", "Raven", "Dawn", "Mirror", "Thorn", "Harbor", "Lantern", "Echo", "Veil",
    ];

    private static string Word(Random random) => Words[random.Next(Words.Length)];
}
