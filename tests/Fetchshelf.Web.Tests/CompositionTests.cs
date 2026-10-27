namespace Fetchshelf.Web.Tests;

/// <summary>
/// Confirms config-management, background-jobs, and web-ui work together in one running process:
/// real config loaded via `ConfigLoader`, a job registered through the real job registry, shown on
/// the Jobs page, triggered via "Run now," with the updated status visible afterward.
/// </summary>
public class CompositionTests
{
    [Fact]
    public async Task EndToEnd_ConfigBackgroundJobsAndWebUi_WorkTogether()
    {
        using var fixture = new FetchshelfWebAppFixture();
        fixture.JobRegistry.Register("example-job", _ => Task.CompletedTask);
        var client = fixture.CreateClient();

        var initialHtml = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();
        Assert.Contains("example-job", initialHtml);
        Assert.Contains("Never run", initialHtml);

        var token = ExtractAntiforgeryToken(initialHtml);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["jobName"] = "example-job",
            ["__RequestVerificationToken"] = token,
        });
        var triggerResponse = await client.PostAsync("/Jobs?handler=Trigger", form);
        triggerResponse.EnsureSuccessStatusCode();

        var updatedHtml = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();
        Assert.Contains("example-job", updatedHtml);
        Assert.Contains("Success", updatedHtml);
        Assert.DoesNotContain("Never run", updatedHtml);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html, @"<input[^>]*name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|<input[^>]*value=""([^""]*)""[^>]*name=""__RequestVerificationToken""");
        if (!match.Success)
        {
            throw new InvalidOperationException("Antiforgery token input not found in page HTML.");
        }

        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}
