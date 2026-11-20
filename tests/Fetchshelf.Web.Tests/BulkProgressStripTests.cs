using Fetchshelf.Web.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// While a bulk action runs or waits, a strip fixed to the bottom of the window shows it and links to the notices at
/// the top of the Torrents page, so the progress stays in sight while the list scrolls.
/// </summary>
public class BulkProgressStripTests
{
    [Fact]
    public async Task Get_Panel_WhileOneRunsAndOneWaits_ShowsTheRunningOneInTheStrip()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("First Book"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash2", OrganizerTestSupport.BuildMetadata("Second Book"));
        using var client = fixture.CreateClient();
        using var scope = fixture.CreateScope();
        // The Link job holding the gate: the first operation starts, then waits at its first torrent; the second queues.
        using var held = await scope.ServiceProvider.GetRequiredService<Fetchshelf.Data.Organizer.LibraryWriteGate>().AcquireAsync();
        var queue = scope.ServiceProvider.GetRequiredService<BulkOperationQueue>();
        var first = queue.Enqueue(BulkAction.Relink, ["hash1", "hash2"]);
        queue.Enqueue(BulkAction.Clean, ["hash2"]);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        // Running is set before the titles are read, and the current title only after: wait for that, not just Running.
        while (first.Progress.Current is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        var panel = await client.GetStringAsync("/torrents?handler=BulkOperations");

        var strip = panel[panel.IndexOf("class=\"bulk-progress\"", StringComparison.Ordinal)..];
        Assert.Contains("href=\"#bulk-operations\"", panel);
        Assert.Contains("<progress max=\"2\" value=\"0\"></progress>", strip);
        Assert.Contains("Relinking 1 of 2: First Book (1 more waiting)", strip);
        Assert.Contains(">Details<", strip);
        Assert.Contains("class=\"bulk-progress\"", await client.GetStringAsync("/torrents"));
    }

    [Fact]
    public async Task Get_Panel_WithNothingRunning_HasNoStrip()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        Assert.DoesNotContain("bulk-progress", await client.GetStringAsync("/torrents?handler=BulkOperations"));
    }

    [Fact]
    public async Task Get_Panel_OnceItHasEnded_HasNoStrip()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("First Book"));
        using var client = fixture.CreateClient();
        using var scope = fixture.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<BulkOperationQueue>();
        var operation = queue.Enqueue(BulkAction.Clean, ["hash1"]);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!operation.HasEnded && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        var panel = await client.GetStringAsync("/torrents?handler=BulkOperations");

        Assert.Contains("Cleaned 1 torrent.", panel);
        Assert.DoesNotContain("bulk-progress", panel);
    }

    [Fact]
    public async Task Stylesheet_MakesTheStripAsWideAsThePagesContent_LikeTheBulkActionBar()
    {
        // It spanned the window, while the bulk-action bar above it is as wide as the table.
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();
        var page = await client.GetStringAsync("/torrents");

        var css = await client.GetStringAsync(System.Text.RegularExpressions.Regex.Match(page, "href=\"(/css/site[^\"]*)\"").Groups[1].Value);

        Assert.Contains("width: calc(min(100%, var(--content-max-width)) - 2 * var(--page-gutter)); margin: 0 auto;", css);
        Assert.Contains("padding: 1.25rem var(--page-gutter) 2rem;", css); // the page's own gutter, the same variable
    }

    [Fact]
    public async Task Script_HidesTheStripWhileTheNoticesAreOnScreen_ByAClassThatOutlivesThePolls()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();
        var page = await client.GetStringAsync("/torrents");
        var scriptUrl = System.Text.RegularExpressions.Regex.Match(page, "src=\"(/js/bulk-bar[^\"]*)\"").Groups[1].Value;

        var script = await client.GetStringAsync(scriptUrl);

        Assert.Contains("document.body.classList.toggle('bulk-notice-in-view'", script);
        Assert.Contains("addEventListener('scroll', placeProgress", script);
    }
}
