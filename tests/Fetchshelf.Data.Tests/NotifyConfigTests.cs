using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>The `[[notify]]` blocks and `web_url`.</summary>
public sealed class NotifyConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-notify-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ConfigurationException LoadFails(string toml)
    {
        File.WriteAllText(ConfigPath, toml);
        return Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));
    }

    private FetchshelfSettings Load(string toml)
    {
        File.WriteAllText(ConfigPath, toml);
        return ConfigLoader.Load(ConfigPath);
    }

    [Fact]
    public void Load_FullBlocks_ReadEverySettingAndTheDefaults()
    {
        var settings = Load("""
            mam_id = ""
            web_url = "https://fetchshelf.example.net"

            [[notify]]
            name = "Phone"
            url = "https://ntfy.sh/fetchshelf-x7k2"
            format = "ntfy"
            token = "tk_abc"
            events = ["mam_refused", "job_failing", "job_recovered", "grabbed"]

            [[notify]]
            url = "http://homeassistant:8123/api/webhook/fetchshelf"
            events = ["all"]
            """);

        Assert.Equal("https://fetchshelf.example.net", settings.WebUrl);
        var targets = NotifyTarget.From(settings);
        Assert.Equal(2, targets.Count);
        Assert.Equal(("Phone", "https://ntfy.sh/fetchshelf-x7k2", NotificationFormat.Ntfy, "tk_abc"), (targets[0].Name, targets[0].Url, targets[0].Format, targets[0].Token));
        Assert.Equal(
            [NotificationEvent.MamRefused, NotificationEvent.JobFailing, NotificationEvent.JobRecovered, NotificationEvent.Grabbed],
            targets[0].Events.OrderBy(e => e));
        // Defaults: the name is notify[i], the format json, no token; "all" is every event.
        Assert.Equal(("notify[1]", NotificationFormat.Json, (string?)null), (targets[1].Name, targets[1].Format, targets[1].Token));
        Assert.Equal(NotificationEvents.All.Count, targets[1].Events.Count);
    }

    [Fact]
    public void Load_NoNotifyBlocks_HasNoTargetsAndNoWebUrl()
    {
        var settings = Load("mam_id = \"\"\n");

        Assert.Empty(NotifyTarget.From(settings));
        Assert.Null(settings.WebUrl);
    }

    [Fact]
    public void Load_UrlFile_IsReadAndTrimmed()
    {
        var urlFile = Path.Combine(_tempDir, "notify.url");
        File.WriteAllText(urlFile, "https://ntfy.sh/secret-topic\n");

        var settings = Load($"""
            mam_id = ""
            [[notify]]
            url_file = "{urlFile.Replace("\\", "/")}"
            events = ["grabbed"]
            """);

        Assert.Equal("https://ntfy.sh/secret-topic", NotifyTarget.From(settings).Single().Url);
    }

    [Fact]
    public void Load_BothUrlAndUrlFile_IsRejectedNamingTheBlock()
    {
        var urlFile = Path.Combine(_tempDir, "notify.url");
        File.WriteAllText(urlFile, "https://x.test/a");

        var error = LoadFails($"""
            mam_id = ""
            [[notify]]
            name = "Phone"
            url = "https://x.test/b"
            url_file = "{urlFile.Replace("\\", "/")}"
            events = ["grabbed"]
            """);

        Assert.Contains("the [[notify]] 'Phone' sets both 'url' and 'url_file'", error.Message);
    }

    [Fact]
    public void Load_UrlFileThatDoesNotExist_IsRejectedWithItsPath()
    {
        var error = LoadFails($"""
            mam_id = ""
            [[notify]]
            url_file = "{Path.Combine(_tempDir, "missing.url").Replace("\\", "/")}"
            events = ["grabbed"]
            """);

        Assert.Contains("'url_file'", error.Message);
        Assert.Contains("does not exist", error.Message);
    }

    [Theory]
    [InlineData("events = [\"grabbed\"]", "the 1st [[notify]] section needs a 'url' or a 'url_file'")]
    [InlineData("url = \"ftp://x.test/a\"\nevents = [\"grabbed\"]", "the 1st [[notify]] section has a 'url' that is not an http or https address")]
    [InlineData("url = \"not a url\"\nevents = [\"grabbed\"]", "has a 'url' that is not an http or https address")]
    [InlineData("url = \"https://x.test/a\"\nformat = \"slack\"\nevents = [\"grabbed\"]", "has an unknown 'format' 'slack'; expected one of json, ntfy, gotify, discord, apprise")]
    [InlineData("url = \"https://x.test/a\"\nevents = []", "has no 'events'")]
    [InlineData("url = \"https://x.test/a\"", "has no 'events'")]
    [InlineData("url = \"https://x.test/a\"\nevents = [\"grabbed\", \"gabbed\"]", "has an unknown event 'gabbed' in 'events'")]
    [InlineData("name = \" Phone\"\nurl = \"https://x.test/a\"\nevents = [\"grabbed\"]", "has a 'name' that is empty or starts or ends with a space")]
    public void Load_EachRuleRejectsWithAMessageNamingTheBlock(string block, string expected)
    {
        var error = LoadFails($"mam_id = \"\"\n[[notify]]\n{block}\n");

        Assert.StartsWith("Invalid configuration:", error.Message);
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void Load_TwoBlocksWithTheSameName_AreRejected()
    {
        var error = LoadFails("""
            mam_id = ""
            [[notify]]
            name = "Phone"
            url = "https://x.test/a"
            events = ["grabbed"]
            [[notify]]
            name = "phone"
            url = "https://x.test/b"
            events = ["grabbed"]
            """);

        Assert.Contains("the [[notify]] 'Phone' and the [[notify]] 'phone' have the same name", error.Message);
    }

    [Fact]
    public void Load_AnErrorNeverRepeatsTheSecretUrl()
    {
        var error = LoadFails("""
            mam_id = ""
            [[notify]]
            url = "ftp://secret.example/topic-x7k2"
            events = ["grabbed"]
            """);

        Assert.DoesNotContain("secret.example", error.Message);
        Assert.DoesNotContain("x7k2", error.Message);
    }

    [Theory]
    [InlineData("fetchshelf.example.net")]
    [InlineData("ftp://fetchshelf.example.net")]
    public void Load_WebUrlThatIsNotAnHttpAddress_IsRejected(string webUrl) =>
        Assert.Contains("'web_url' must be an http or https address", LoadFails($"mam_id = \"\"\nweb_url = \"{webUrl}\"\n").Message);

    [Fact]
    public void Load_UnknownKeyInANotifyBlock_IsRejected() =>
        Assert.Contains("urll", LoadFails("mam_id = \"\"\n[[notify]]\nurll = \"https://x.test/a\"\nevents = [\"all\"]\n").Message);
}
