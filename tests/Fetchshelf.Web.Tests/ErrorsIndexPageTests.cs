using System.Text.Json;
using System.Text.RegularExpressions;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

public partial class ErrorsIndexPageTests
{
    private static async Task<ErroredTorrentEntity> SeedGrabberErrorAsync(FetchshelfWebAppFixture fixture, bool restorable)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var entity = new ErroredTorrentEntity
        {
            Stage = ErrorStage.Grabber,
            Key = "1",
            ErrorText = "Download link is stale and could not be refreshed.",
            OccurredAt = DateTimeOffset.UtcNow,
            DisplayMetadataJson = restorable
                ? JsonSerializer.Serialize(new GrabberErrorRestorePayload
                {
                    MamId = 1,
                    DownloadLink = "dl1",
                    CostDecision = "Ratio",
                    MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(OrganizerTestSupport.BuildMetadata("The Way of Kings")),
                    NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize("The Way of Kings"),
                })
                : null,
        };
        dbContext.ErroredTorrents.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity;
    }

    [Fact]
    public async Task Get_DownloadError_ShowsItsStageErrorAndTorrent()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedGrabberErrorAsync(fixture, restorable: false);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/errors")).Content.ReadAsStringAsync();

        Assert.Contains("Download", html);
        Assert.Contains("stale", html);
        Assert.Contains("MAM id 1", html);
    }

    [Fact]
    public async Task Get_RestorableDownloadErrorWithoutStoredTitle_ShowsTitleFromItsSelection()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedGrabberErrorAsync(fixture, restorable: true);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/errors")).Content.ReadAsStringAsync();

        Assert.Contains("The Way of Kings", html);
        Assert.Contains("Retry download", html);
    }

    [Fact]
    public async Task Get_LinkErrorForTrackedTorrent_ShowsTitleAndLinksToTheTorrent()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Torrents.Add(new TorrentEntity
            {
                Id = "abc123",
                MamId = 2,
                Title = "Mistborn",
                NormalizedTitleKey = "mistborn",
                MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(OrganizerTestSupport.BuildMetadata("Mistborn")),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            dbContext.ErroredTorrents.Add(new ErroredTorrentEntity
            {
                Stage = ErrorStage.Linker,
                Key = "abc123",
                ErrorText = "Torrent has no author; cannot be linked.",
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await dbContext.SaveChangesAsync();
        }

        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/errors")).Content.ReadAsStringAsync();

        Assert.Contains("Link", html);
        Assert.Contains("Info-hash abc123", html);
        Assert.Contains("href=\"/torrents/abc123\"", html);
        Assert.Contains("Mistborn", html);
    }

    [Fact]
    public async Task Get_NoAuthorLinkErrorWithoutRecord_OffersEditForItsInfoHash()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.ErroredTorrents.Add(new ErroredTorrentEntity
            {
                Stage = ErrorStage.Linker,
                Key = "abc123",
                Title = "Exploitation Film Posters",
                ErrorText = "Torrent has no author; cannot be linked.",
                DisplayMetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(
                    OrganizerTestSupport.BuildMetadata("Exploitation Film Posters", authors: [])),
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await dbContext.SaveChangesAsync();
        }

        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/errors")).Content.ReadAsStringAsync();

        Assert.Contains("href=\"/torrents/abc123/edit\"", html);
    }

    [Fact]
    public async Task Get_LinkErrorWithoutStoredMetadata_OffersNoEdit()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.ErroredTorrents.Add(new ErroredTorrentEntity
            {
                Stage = ErrorStage.Linker,
                Key = "abc123",
                ErrorText = "MAM has no torrent with this info-hash.",
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await dbContext.SaveChangesAsync();
        }

        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/errors")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("/torrents/abc123/edit", html);
    }

    [Fact]
    public async Task Post_RemoveSelected_RemovesTheTickedErrors()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var entity = await SeedGrabberErrorAsync(fixture, restorable: false);
        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/errors")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["selectedIds"] = entity.Id.ToString(),
            ["__RequestVerificationToken"] = token,
        });
        await client.PostAsync("/errors?handler=RemoveSelected", form);

        var afterHtml = await (await client.GetAsync("/errors")).Content.ReadAsStringAsync();
        Assert.Contains("No errors", afterHtml);
    }

    [Fact]
    public async Task Post_Restore_RecreatesSelectionAndRemovesError()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var entity = await SeedGrabberErrorAsync(fixture, restorable: true);
        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/errors")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = entity.Id.ToString(),
            ["__RequestVerificationToken"] = token,
        });
        await client.PostAsync("/errors?handler=Restore", form);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        Assert.Empty(dbContext.ErroredTorrents);
        var selected = Assert.Single(dbContext.SelectedTorrents);
        Assert.Equal(1, selected.MamId);
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
