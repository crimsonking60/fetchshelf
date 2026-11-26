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

    [Theory]
    [InlineData("Torrents/_TorrentListPartial.cshtml")]
    [InlineData("Queue/Index.cshtml")]
    [InlineData("Errors/Index.cshtml")]
    [InlineData("Duplicates/Index.cshtml")]
    [InlineData("Following/_FindList.cshtml")]
    public void EveryCountedConfirmation_NamesWhatIsCounted(string page)
    {
        // "Remove 1 selected from Fetchshelf?" left the reader guessing what was selected.
        var markup = File.ReadAllText(Path.Combine(WebProjectDirectory(), "Pages", page));

        var counted = System.Text.RegularExpressions.Regex.Matches(markup, @"<button[^>]*data-confirm=""[^""]*\{n\}[^>]*>");

        Assert.NotEmpty(counted);
        Assert.All(counted, button => Assert.Contains("data-confirm-noun=\"", button.Value));
    }

    [Fact]
    public async Task Script_NamesTheNounInTheSingularOrPlural()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();
        var page = await client.GetStringAsync("/events");
        var scriptUrl = System.Text.RegularExpressions.Regex.Match(page, "src=\"(/js/confirm[^\"]*)\"").Groups[1].Value;

        var script = await client.GetStringAsync(scriptUrl);

        Assert.Contains("source.dataset.confirmNoun", script);
        Assert.Contains("count === 1", script);
    }

    private static string WebProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Fetchshelf.Web")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("The repository root was not found."), "src", "Fetchshelf.Web");
    }
}
