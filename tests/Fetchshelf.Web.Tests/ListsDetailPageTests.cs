using System.Text.RegularExpressions;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>Covers task 8.2: the List detail page.</summary>
public partial class ListsDetailPageTests
{
    private const string ListId = "1:to-read";

    private static async Task SeedListAsync(FetchshelfWebAppFixture fixture, params ListItemEntity[] items)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        dbContext.Lists.Add(new ListEntity { Id = ListId, Name = "To Read", ListType = "Goodreads", CreatedAt = DateTimeOffset.UtcNow });
        dbContext.ListItems.AddRange(items);
        await dbContext.SaveChangesAsync();
    }

    private static ListItemEntity BuildItem(string guid, string title, bool allowAudio = true, bool allowEbook = false, string? audioStatus = null) => new()
    {
        ListId = ListId,
        Guid = guid,
        BookTitle = title,
        AuthorsJson = "[\"Brandon Sanderson\"]",
        CoverUrl = "https://images.example.com/cover.jpg",
        AllowAudio = allowAudio,
        AllowEbook = allowEbook,
        AudioStatus = audioStatus,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Get_ShowFilterAudioMissing_NarrowsToUnresolvedAudioItems()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedListAsync(
            fixture,
            BuildItem("guid-missing", "Mistborn"),
            BuildItem("guid-resolved", "Warbreaker", audioStatus: "Selected"));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/lists/1:to-read?show=audio")).Content.ReadAsStringAsync();

        Assert.Contains("Mistborn", html);
        Assert.DoesNotContain("Warbreaker", html);
    }

    [Fact]
    public async Task Get_ShowAll_IncludesEveryItem()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedListAsync(
            fixture,
            BuildItem("guid-missing", "Mistborn"),
            BuildItem("guid-resolved", "Warbreaker", audioStatus: "Selected"));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/lists/1:to-read")).Content.ReadAsStringAsync();

        Assert.Contains("Mistborn", html);
        Assert.Contains("Warbreaker", html);
    }

    [Fact]
    public async Task Post_MarkDone_SetsMarkedDoneAtAndRemovesAction()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedListAsync(fixture, BuildItem("guid-missing", "Mistborn"));
        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/lists/1:to-read")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["guid"] = "guid-missing",
            ["__RequestVerificationToken"] = token,
        });
        await client.PostAsync("/lists/1:to-read?handler=MarkDone", form);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var item = await dbContext.ListItems.FindAsync(ListId, "guid-missing");
        Assert.NotNull(item!.MarkedDoneAt);
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
