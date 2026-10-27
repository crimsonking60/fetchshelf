using System.Text.RegularExpressions;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>Covers task 10.1: the Selected page.</summary>
public partial class SelectedIndexPageTests
{
    private static async Task<SelectedTorrentEntity> SeedSelectedAsync(FetchshelfWebAppFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var entity = new SelectedTorrentEntity
        {
            MamId = 1,
            DownloadLink = "dl1",
            CostDecision = "Ratio",
            Category = "audiobooks",
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(OrganizerTestSupport.BuildMetadata("The Way of Kings")),
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize("The Way of Kings"),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.SelectedTorrents.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity;
    }

    [Fact]
    public async Task Get_ListsActiveSelections()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedSelectedAsync(fixture);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/selected")).Content.ReadAsStringAsync();

        Assert.Contains("The Way of Kings", html);
    }

    [Fact]
    public async Task Post_Remove_RemovesSelectionFromQueue()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var entity = await SeedSelectedAsync(fixture);
        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/selected")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = entity.Id.ToString(),
            ["__RequestVerificationToken"] = token,
        });
        await client.PostAsync("/selected?handler=Remove", form);

        var afterHtml = await (await client.GetAsync("/selected")).Content.ReadAsStringAsync();
        Assert.Contains("No torrents are selected", afterHtml);
    }

    [Fact]
    public async Task Post_UpdateCategory_PersistsNewCategory()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var entity = await SeedSelectedAsync(fixture);
        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/selected")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = entity.Id.ToString(),
            ["category"] = "ebooks",
            ["tags"] = "a,b",
            ["__RequestVerificationToken"] = token,
        });
        await client.PostAsync("/selected?handler=Update", form);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var updated = await dbContext.SelectedTorrents.FindAsync(entity.Id);
        Assert.Equal("ebooks", updated!.Category);
    }

    [GeneratedRegex(@"<input[^>]*name=""__RequestVerificationToken""[^>]*>")]
    private static partial Regex AntiforgeryInputPattern();

    [GeneratedRegex(@"value=""([^""]*)""")]
    private static partial Regex ValueAttributePattern();

    private static string ExtractAntiforgeryToken(string html)
    {
        var inputMatch = AntiforgeryInputPattern().Match(html);
        var valueMatch = ValueAttributePattern().Match(inputMatch.Value);
        return valueMatch.Groups[1].Value;
    }
}
