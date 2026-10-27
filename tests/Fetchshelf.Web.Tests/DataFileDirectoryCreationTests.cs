namespace Fetchshelf.Web.Tests;

/// <summary>Covers task 3.1: the database file's parent directory is created if missing.</summary>
public class DataFileDirectoryCreationTests
{
    [Fact]
    public async Task HomePage_DataFileInNonExistentNestedDirectory_StartsAndServesInsteadOfFailing()
    {
        using var fixture = new FetchshelfWebAppFixture(dataFileRelativePath: Path.Combine("nested", "subdir", "data.db"));
        var client = fixture.CreateClient();

        // Hits the Jobs page rather than "/" since this is only about startup/DB-directory
        // creation, not the home (Search) page's MaM-dependent behavior.
        var response = await client.GetAsync("/Jobs");

        response.EnsureSuccessStatusCode();
    }
}
