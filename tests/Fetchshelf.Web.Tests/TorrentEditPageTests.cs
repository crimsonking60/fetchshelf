using System.Text.RegularExpressions;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

public partial class TorrentEditPageTests
{
    [Fact]
    public async Task Post_EditSubmission_UpdatesStoredMetadataAndSourceBecomesManual()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("Original Title"));
        var client = fixture.CreateClient();

        var editHtml = await (await client.GetAsync("/torrents/hash1/edit")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(editHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "New Title",
            ["Input.Authors"] = "Jane Doe",
            ["Input.Narrators"] = "",
            ["Input.Series"] = "",
            ["Input.Language"] = "English",
            ["Input.Category"] = "AudiobookGeneralFiction",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/torrents/hash1/edit", form);
        response.EnsureSuccessStatusCode();

        var detailHtml = await (await client.GetAsync("/torrents/hash1")).Content.ReadAsStringAsync();
        Assert.Contains("New Title", detailHtml);
        Assert.Contains("Jane Doe", detailHtml);
        Assert.Contains("Manual", detailHtml);
    }

    [Fact]
    public async Task Get_TorrentOnlyKnownFromANoAuthorLinkError_PrefillsWhatMamSaid()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedNoAuthorLinkErrorAsync(fixture, "hash2");
        var client = fixture.CreateClient();

        var editHtml = await (await client.GetAsync("/torrents/hash2/edit")).Content.ReadAsStringAsync();

        Assert.Contains("value=\"Exploitation Film Posters\"", editHtml);
    }

    [Fact]
    public async Task Post_TorrentOnlyKnownFromANoAuthorLinkError_CreatesTheRecordKeepingMamsOtherDetails()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedNoAuthorLinkErrorAsync(fixture, "hash2");
        var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await (await client.GetAsync("/torrents/hash2/edit")).Content.ReadAsStringAsync());

        await client.PostAsync("/torrents/hash2/edit", EditForm(token, "Exploitation Film Posters", "Graham Marsh"));

        var metadata = await StoredMetadataAsync(fixture, "hash2");
        Assert.Equal(["Graham Marsh"], metadata.Authors);
        Assert.Equal(42, metadata.MamId);
        Assert.Equal("Posters from the grindhouse years.", metadata.Description);
        Assert.Equal(MetadataSource.Manual, metadata.MetadataSource);
    }

    [Fact]
    public async Task Post_EditOfExistingRecord_KeepsTheFieldsTheFormDoesNotShow()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var original = OrganizerTestSupport.BuildMetadata("Original Title") with
        {
            Description = "A long description.",
            Isbn = "9780000000000",
            FileTypes = ["m4b"],
        };
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", original);
        var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await (await client.GetAsync("/torrents/hash1/edit")).Content.ReadAsStringAsync());

        await client.PostAsync("/torrents/hash1/edit", EditForm(token, "New Title", "Jane Doe"));

        var metadata = await StoredMetadataAsync(fixture, "hash1");
        Assert.Equal("New Title", metadata.Title);
        Assert.Equal("A long description.", metadata.Description);
        Assert.Equal("9780000000000", metadata.Isbn);
        Assert.Equal(["m4b"], metadata.FileTypes);
    }

    private static async Task SeedNoAuthorLinkErrorAsync(FetchshelfWebAppFixture fixture, string hash)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var metadata = OrganizerTestSupport.BuildMetadata("Exploitation Film Posters", authors: []) with
        {
            MamId = 42,
            Description = "Posters from the grindhouse years.",
        };
        dbContext.ErroredTorrents.Add(new ErroredTorrentEntity
        {
            Stage = ErrorStage.Linker,
            Key = hash,
            Title = "Exploitation Film Posters",
            ErrorText = "Torrent has no author; cannot be linked.",
            DisplayMetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<TorrentMetadata> StoredMetadataAsync(FetchshelfWebAppFixture fixture, string hash)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var entity = await dbContext.Torrents.SingleAsync(t => t.Id == hash);
        return TorrentMetadataSerializer.Deserialize(entity.MetadataJson);
    }

    private static FormUrlEncodedContent EditForm(string token, string title, string authors) => new(new Dictionary<string, string>
    {
        ["Input.Title"] = title,
        ["Input.Authors"] = authors,
        ["Input.Narrators"] = "",
        ["Input.Series"] = "",
        ["Input.Language"] = "English",
        ["Input.Category"] = "AudiobookGeneralFiction",
        ["__RequestVerificationToken"] = token,
    });

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""")]
    private static partial Regex AntiforgeryTokenPattern();

    private static async Task<HttpResponseMessage> PostEditAsync(HttpClient client, string id, params (string Key, string Value)[] extra)
    {
        var token = ExtractAntiforgeryToken(await (await client.GetAsync($"/torrents/{id}/edit")).Content.ReadAsStringAsync());
        List<KeyValuePair<string, string>> fields =
        [
            new("Input.Title", "Genre Book"), new("Input.Authors", "Jane Doe"), new("Input.Narrators", ""), new("Input.Series", ""),
            new("Input.Language", "English"), new("Input.Category", "EbookHistory"), new("__RequestVerificationToken", token),
        ];
        fields.AddRange(extra.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value)));
        using var form = new FormUrlEncodedContent(fields);
        return await client.PostAsync($"/torrents/{id}/edit", form);
    }

    private static TorrentMetadata GenreBook() => OrganizerTestSupport.BuildMetadata("Genre Book") with
    {
        FictionCategory = FictionCategory.Fiction,
        GenreCategories = new HashSet<GenreCategory> { GenreCategory.Historical },
    };

    private static async Task<TorrentMetadata> StoredAsync(FetchshelfWebAppFixture fixture, string id)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        return TorrentMetadataSerializer.Deserialize((await dbContext.Torrents.AsNoTracking().SingleAsync(t => t.Id == id)).MetadataJson);
    }

    [Fact]
    public async Task GenresOn_TheMainCategoryAndGenresCanBeEdited_ByMamsNames()
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"\"\ngenres = true\n");
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", GenreBook());
        var client = fixture.CreateClient();

        var editHtml = await client.GetStringAsync("/torrents/hash1/edit");
        var response = await PostEditAsync(client, "hash1",
            ("Input.GenresShown", "true"), ("Input.Fiction", "Nonfiction"), ("Input.Genres", "17"), ("Input.Genres", "63"));

        Assert.Contains("<option value=\"Fiction\" selected=\"selected\">Fiction</option>", editHtml);
        Assert.Matches("value=\"17\" checked=\"checked\" />\\s*Historical", editHtml);
        Assert.Contains("Erotica/Sexual Content", editHtml);
        response.EnsureSuccessStatusCode();
        var stored = await StoredAsync(fixture, "hash1");
        Assert.Equal(FictionCategory.Nonfiction, stored.FictionCategory);
        Assert.Equal([GenreCategory.Historical, GenreCategory.Military], stored.GenreCategories.Order());
    }

    [Fact]
    public async Task GenresOff_TheFormHasNoGenres_AndSavingKeepsThem()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", GenreBook());
        var client = fixture.CreateClient();

        var editHtml = await client.GetStringAsync("/torrents/hash1/edit");
        (await PostEditAsync(client, "hash1")).EnsureSuccessStatusCode();

        Assert.DoesNotContain("edit-genres", editHtml);
        var stored = await StoredAsync(fixture, "hash1");
        Assert.Equal(FictionCategory.Fiction, stored.FictionCategory);
        Assert.Equal([GenreCategory.Historical], stored.GenreCategories);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenPattern().Match(html);
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}
