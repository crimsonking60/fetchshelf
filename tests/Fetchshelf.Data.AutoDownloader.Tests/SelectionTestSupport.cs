using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader.Tests;

internal static class SelectionTestSupport
{
    public static readonly FormatPreferenceLists PreferenceLists = new()
    {
        AudioTypes = ["m4b", "m4a", "mp3"],
        EbookTypes = ["epub", "pdf"],
        MusicTypes = ["mp3"],
        RadioTypes = ["mp3"],
    };

    public static FetchshelfDbContext CreateContext(string dbPath)
    {
        var options = new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        var context = new FetchshelfDbContext(options);
        DatabaseMigrator.Migrate(context);
        return context;
    }

    public static MamTorrentRecord BuildRecord(
        long mamId = 1,
        string title = "The Way of Kings",
        string authorName = "Brandon Sanderson",
        IReadOnlyList<string>? fileTypes = null,
        bool isVip = false,
        bool isPersonalFreeleech = false,
        bool isFree = false,
        string uploaderName = "uploader",
        long seeders = 5,
        long leechers = 1,
        long timesCompleted = 10) => new()
        {
            MamId = mamId,
            Title = title,
            Authors = new Dictionary<long, string> { [1] = authorName },
            Narrators = new Dictionary<long, string>(),
            Series = [],
            MediaTypeId = (int)MediaType.Audiobook,
            OldMainCategoryId = 0,
            FictionCategoryId = 1,
            LegacyCategoryId = (int)LegacyCategory.AudiobookGeneralFiction,
            GenreCategoryIds = [],
            LanguageId = (int)Language.English,
            FlagsRaw = 0,
            FileTypesRaw = string.Join(' ', fileTypes ?? ["m4b"]),
            FileCount = 1,
            SizeRaw = "100 MiB",
            TagsRaw = string.Empty,
            UploaderId = 1,
            UploaderName = uploaderName,
            UploadedAt = DateTimeOffset.UtcNow,
            Seeders = seeders,
            Leechers = leechers,
            TimesCompleted = timesCompleted,
            IsFree = isFree,
            IsPersonalFreeleech = isPersonalFreeleech,
            IsFreeleechVip = false,
            IsVip = isVip,
            VipExpireUnixSeconds = 0,
            DownloadHash = "dlhash",
        };
}
