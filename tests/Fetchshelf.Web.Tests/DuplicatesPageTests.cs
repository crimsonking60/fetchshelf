using System.Text.RegularExpressions;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>The Duplicates page: torrents skipped because an equal or better copy exists.</summary>
public partial class DuplicatesPageTests
{
    private static async Task<DuplicateTorrentEntity> SeedDuplicateAsync(
        FetchshelfWebAppFixture fixture, string title, long mamId, string duplicateOf)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var entity = new DuplicateTorrentEntity
        {
            MamId = mamId,
            MetadataJson = TorrentMetadataSerializer.Serialize(OrganizerTestSupport.BuildMetadata(title)),
            DuplicateOfTorrentId = duplicateOf,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.DuplicateTorrents.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity;
    }

    private static async Task SeedSelectionAsync(FetchshelfWebAppFixture fixture, string title, long mamId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        dbContext.SelectedTorrents.Add(new SelectedTorrentEntity
        {
            MamId = mamId,
            DownloadLink = "dl",
            CostDecision = "Ratio",
            MetadataJson = TorrentMetadataSerializer.Serialize(OrganizerTestSupport.BuildMetadata(title)),
            NormalizedTitleKey = TitleNormalizer.Normalize(title),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_NoDuplicates_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/duplicates");

        Assert.Contains("No duplicates.", html);
    }

    [Fact]
    public async Task Get_AnyPage_HasDuplicatesInTheNavigation()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("href=\"/Duplicates\">Duplicates</a>", html);
    }

    [Fact]
    public async Task Get_DuplicateOfALibraryTorrent_LinksToThatTorrent()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("Kept Book"));
        await SeedDuplicateAsync(fixture, "Skipped Book", mamId: 7, duplicateOf: "hash1");
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/duplicates");

        Assert.Contains("Skipped Book", html);
        Assert.Contains("href=\"https://www.myanonamouse.net/t/7\"", html);
        Assert.Contains("<a href=\"/torrents/hash1\">Kept Book</a>", html);
    }

    [Fact]
    public async Task Get_DuplicateOfASelection_NamesTheSelection()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedSelectionAsync(fixture, "Queued Book", mamId: 42);
        await SeedDuplicateAsync(fixture, "Skipped Book", mamId: 7, duplicateOf: "42");
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/duplicates");

        Assert.Contains("Queued Book (selected)", html);
        Assert.Contains("href=\"https://www.myanonamouse.net/t/42\"", html);
    }

    [Fact]
    public async Task Post_RemoveSelected_DeletesOnlyTheTickedRows()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var removed = await SeedDuplicateAsync(fixture, "First Book", mamId: 1, duplicateOf: "11");
        await SeedDuplicateAsync(fixture, "Second Book", mamId: 2, duplicateOf: "12");
        var client = fixture.CreateClient();
        var token = AntiforgeryToken().Match(await client.GetStringAsync("/duplicates")).Groups[1].Value;

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["selectedIds"] = removed.Id.ToString(),
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/duplicates?handler=RemoveSelected", form);

        response.EnsureSuccessStatusCode();
        using var scope = fixture.CreateScope();
        var remaining = await scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().DuplicateTorrents.ToListAsync();
        Assert.Equal(2, Assert.Single(remaining).MamId);
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]*)""")]
    private static partial Regex AntiforgeryToken();
}
