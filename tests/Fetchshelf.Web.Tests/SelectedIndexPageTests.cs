using System.Text.RegularExpressions;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>Covers task 10.1: the Selected page.</summary>
public partial class SelectedIndexPageTests
{
    private static async Task<SelectedTorrentEntity> SeedSelectedAsync(
        FetchshelfWebAppFixture fixture, string title = "The Way of Kings", long mamId = 1)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var entity = new SelectedTorrentEntity
        {
            MamId = mamId,
            DownloadLink = $"dl{mamId}",
            CostDecision = "Ratio",
            Category = "audiobooks",
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(OrganizerTestSupport.BuildMetadata(title)),
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(title),
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
    public async Task Get_EachRowHasACheckbox_AndOneRemoveSelectedButtonSitsBelowTheTable()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedSelectedAsync(fixture, "First Book", mamId: 1);
        await SeedSelectedAsync(fixture, "Second Book", mamId: 2);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/selected");

        Assert.Equal(2, Regex.Matches(html, "class=\"selection-select\" type=\"checkbox\" name=\"selectedIds\"").Count);
        Assert.Single(Regex.Matches(html, ">Remove selected</button>"));
        Assert.True(html.IndexOf(">Remove selected</button>", StringComparison.Ordinal) > html.IndexOf("</table>", StringComparison.Ordinal));
        Assert.DoesNotContain("handler=Remove\"", html); // the old per-row Remove is gone
    }

    [Fact]
    public async Task Get_EachRowKeepsItsOwnUpdateButton()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var first = await SeedSelectedAsync(fixture, "First Book", mamId: 1);
        var second = await SeedSelectedAsync(fixture, "Second Book", mamId: 2);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/selected");

        Assert.Equal(2, Regex.Matches(html, "<button form=\"update-\\d+\" type=\"submit\">Update</button>").Count);
        Assert.Contains($"id=\"update-{first.Id}\"", html);
        Assert.Contains($"id=\"update-{second.Id}\"", html);
    }

    [Fact]
    public async Task Post_RemoveSelected_RemovesOnlyTheTickedSelectionsFromTheQueue()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var first = await SeedSelectedAsync(fixture, "First Book", mamId: 1);
        var second = await SeedSelectedAsync(fixture, "Second Book", mamId: 2);
        var third = await SeedSelectedAsync(fixture, "Third Book", mamId: 3);
        var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/selected"));

        using var form = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("selectedIds", first.Id.ToString()),
            new("selectedIds", third.Id.ToString()),
            new("__RequestVerificationToken", token),
        });
        await client.PostAsync("/selected?handler=RemoveSelected", form);

        var afterHtml = await client.GetStringAsync("/selected");
        Assert.Contains("Second Book", afterHtml);
        Assert.DoesNotContain("First Book", afterHtml);
        Assert.DoesNotContain("Third Book", afterHtml);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        Assert.NotNull((await dbContext.SelectedTorrents.FindAsync(first.Id))!.RemovedAt);
        Assert.Null((await dbContext.SelectedTorrents.FindAsync(second.Id))!.RemovedAt);
        Assert.NotNull((await dbContext.SelectedTorrents.FindAsync(third.Id))!.RemovedAt);
    }

    [Fact]
    public async Task Post_RemoveSelected_NothingTicked_ChangesNothing()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedSelectedAsync(fixture);
        var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/selected"));

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        await client.PostAsync("/selected?handler=RemoveSelected", form);

        Assert.Contains("The Way of Kings", await client.GetStringAsync("/selected"));
    }

    [Fact]
    public async Task Post_RemoveSelected_AlreadyRemovedSelection_KeepsItsOriginalRemovalTime()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var entity = await SeedSelectedAsync(fixture);
        var removedAt = DateTimeOffset.UtcNow.AddDays(-3);
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            (await dbContext.SelectedTorrents.FindAsync(entity.Id))!.RemovedAt = removedAt;
            await dbContext.SaveChangesAsync();
        }

        var client = fixture.CreateClient();
        var tokenSource = await client.GetStringAsync("/torrents"); // /selected has no form to take a token from while its list is empty
        using var form = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("selectedIds", entity.Id.ToString()),
            new("__RequestVerificationToken", ExtractAntiforgeryToken(tokenSource)),
        });
        await client.PostAsync("/selected?handler=RemoveSelected", form);

        using var verifyScope = fixture.CreateScope();
        var stored = await verifyScope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().SelectedTorrents.FindAsync(entity.Id);
        Assert.Equal(removedAt.ToUnixTimeSeconds(), stored!.RemovedAt!.Value.ToUnixTimeSeconds());
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
