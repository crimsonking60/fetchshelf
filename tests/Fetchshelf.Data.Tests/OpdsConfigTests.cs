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
}
