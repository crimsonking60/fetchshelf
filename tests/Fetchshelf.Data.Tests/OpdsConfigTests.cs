using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class OpdsConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-opds-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FetchshelfSettings Load(string toml)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);
        return ConfigLoader.Load(ConfigPath);
    }

    private const string Libraries = """

        [[library]]
        name = "Ebooks"
        category = "ebooks"
        library_dir = "/library/ebooks"

        [[library]]
        category = "comics"
        library_dir = "/library/comics"

        """;

    [Fact]
    public void Load_NoOpdsSection_LeavesOpdsOff() => Assert.Null(Load(Libraries).Opds);

    [Fact]
    public void Load_EmptyOpdsSection_ServesEveryLibrary()
    {
        var opds = Load(Libraries + "[opds]\n").Opds;

        Assert.NotNull(opds);
        Assert.Empty(opds.Libraries);
    }

    [Fact]
    public void Load_OpdsLibraries_AreReadByName()
    {
        var opds = Load(Libraries + "[opds]\nlibraries = [\"Ebooks\"]\n").Opds;

        Assert.Equal(["Ebooks"], opds!.Libraries);
    }

    [Fact]
    public void Load_OpdsLibraryNoLibraryHas_Throws()
    {
        var exception = Assert.Throws<ConfigurationException>(() => Load(Libraries + "[opds]\nlibraries = [\"Comics\"]\n"));

        Assert.Contains("'Comics'", exception.Message);
        Assert.Contains("[opds]", exception.Message);
    }

    [Fact]
    public void Load_UnknownOpdsKey_Throws()
    {
        var exception = Assert.Throws<ConfigurationException>(() => Load(Libraries + "[opds]\nlibrary = [\"Ebooks\"]\n"));

        Assert.Contains("library", exception.Message);
    }

    [Fact]
    public void Load_OpdsLogin_IsRead()
    {
        var opds = Load(Libraries + "[opds]\nusername = \"reader\"\npassword = \"s3cret\"\n").Opds!;

        Assert.Equal(("reader", "s3cret", true), (opds.Username, opds.Password, opds.RequiresLogin));
    }

    [Fact]
    public void Load_NoOpdsLogin_RequiresNone() => Assert.False(Load(Libraries + "[opds]\n").Opds!.RequiresLogin);

    [Theory]
    [InlineData("username = \"reader\"\n")]
    [InlineData("password = \"s3cret\"\n")]
    public void Load_OpdsUsernameOrPasswordAlone_IsRefused(string login)
    {
        var exception = Assert.Throws<ConfigurationException>(() => Load(Libraries + "[opds]\n" + login));

        Assert.Contains("needs both 'username' and 'password'", exception.Message);
    }

    [Fact]
    public void Load_OpdsUsernameWithAColon_IsRefused() =>
        Assert.Contains("can't contain ':'", Assert.Throws<ConfigurationException>(() => Load(Libraries + "[opds]\nusername = \"a:b\"\npassword = \"p\"\n")).Message);

    [Fact]
    public void Load_OpdsPasswordFile_IsRead_WithoutItsNewline()
    {
        var file = Path.Combine(_tempDir, "opds_password");
        File.WriteAllText(file, "from-file\n");

        var opds = Load(Libraries + $"[opds]\nusername = \"reader\"\npassword_file = \"{file}\"\n").Opds!;

        Assert.Equal("from-file", opds.Password);
    }

    [Fact]
    public void Load_OpdsPasswordAndPasswordFile_IsRefused()
    {
        var file = Path.Combine(_tempDir, "opds_password");
        File.WriteAllText(file, "from-file");

        var exception = Assert.Throws<ConfigurationException>(
            () => Load(Libraries + $"[opds]\nusername = \"reader\"\npassword = \"p\"\npassword_file = \"{file}\"\n"));

        Assert.Contains("either 'password' or 'password_file' in [opds], not both", exception.Message);
    }

    [Theory]
    [InlineData("fetchshelf.lan")]
    [InlineData("*.example.org")]
    [InlineData("192.168.1.10")]
    [InlineData("[fd00::10]")]
    [InlineData("books")]
    public void Load_AllowedHosts_TakesHostNamesAndAddresses(string host) =>
        Assert.Equal([host], Load($"allowed_hosts = [\"{host}\"]\n").AllowedHosts);

    [Theory]
    [InlineData("http://fetchshelf.lan:3157/", "fetchshelf.lan")]
    [InlineData("fetchshelf.lan:3157", "fetchshelf.lan")]
    [InlineData("fetchshelf.lan/opds", "fetchshelf.lan")]
    [InlineData("https://[fd00::10]:3157", "[fd00::10]")]
    [InlineData("fetch shelf", "fetch")]
    public void Load_AllowedHostsWithASchemePortOrPath_IsRefused_SayingWhatToWrite(string entry, string suggested)
    {
        var exception = Assert.Throws<ConfigurationException>(() => Load($"allowed_hosts = [\"{entry}\"]\n"));

        Assert.Equal(
            $"Invalid configuration: allowed_hosts: write '{suggested}', not '{entry}': a host name or IP address only, without a scheme, port or path.",
            exception.Message);
    }
}
