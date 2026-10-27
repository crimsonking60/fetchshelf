using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class SnatchlistConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-snatchlist-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_DisallowedCost_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[snatchlist]]
            type = "unsat"
            cost = "ratio"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("cost", exception.Message);
    }

    [Fact]
    public void Load_LanguagesFilter_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[snatchlist]]
            type = "unsat"
            languages = ["English"]
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("languages", exception.Message);
    }

    [Fact]
    public void Load_UploadDateFilter_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[snatchlist]]
            type = "unsat"
            uploaded_after = "2026-01-01"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("uploaded_after", exception.Message);
    }

    [Fact]
    public void Load_EntryWithOnlyType_UsesGlobalSearchInterval()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""
            search_interval = 45

            [[snatchlist]]
            type = "unsat"
            """);

        var entry = Assert.Single(ConfigLoader.Load(ConfigPath).Snatchlist);

        Assert.Equal(SnatchlistType.Unsat, entry.Type);
        Assert.Null(entry.SearchInterval);
    }
}
