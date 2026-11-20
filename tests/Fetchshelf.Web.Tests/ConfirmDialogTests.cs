namespace Fetchshelf.Web.Tests;

/// <summary>Destructive bulk actions ask first, through confirm.js's dialog, and sit apart from the other buttons.</summary>
public class ConfirmDialogTests
{
    [Fact]
    public async Task Get_TorrentsWithARow_ConfirmsUnlinkAndRemoveAndSetsThemApart()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("A Book", ["An Author"]));
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents");

        Assert.Contains("class=\"bulk-danger\"", html);
        Assert.Matches(@"<button[^>]*value=""unlink""[^>]*data-confirm=""Unlink \{n\}", html);
        Assert.Matches(@"<button[^>]*value=""removeRecord""[^>]*data-confirm=""Remove the records of \{n\}", html);
        Assert.Contains("confirm.", html);
    }
}
