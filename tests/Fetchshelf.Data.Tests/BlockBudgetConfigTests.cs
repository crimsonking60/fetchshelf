using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>`unsat_buffer`, `wedge_buffer` and `max_active_downloads` in `[[hardcover_list]]`, `[follow]` and `[upgrade]`.</summary>
public sealed class BlockBudgetConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-block-budget-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    /// <summary>Each section, with <c>{0}</c> where its own settings go.</summary>
    public static TheoryData<string, string> Sections => new()
    {
        { "hardcover_list", "[hardcover]\ntoken = \"t\"\n\n[[hardcover_list]]\nstatus = \"want_to_read\"\n{0}\n[[hardcover_list.grab]]\ncost = \"free\"\n" },
        { "follow", "[follow]\n{0}\n[[follow.grab]]\ncost = \"free\"\n" },
        { "upgrade", "[upgrade]\n{0}\n" },
    };

    private FetchshelfSettings Load(string section, string settings)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + section.Replace("{0}", settings, StringComparison.Ordinal));
        return ConfigLoader.Load(ConfigPath);
    }

    private static (int? Unsat, int? Wedge, int? MaxActive) Budgets(FetchshelfSettings settings, string name) => name switch
    {
        "hardcover_list" => (settings.HardcoverList[0].UnsatBuffer, settings.HardcoverList[0].WedgeBuffer, settings.HardcoverList[0].MaxActiveDownloads),
        "follow" => (settings.Follow!.UnsatBuffer, settings.Follow.WedgeBuffer, settings.Follow.MaxActiveDownloads),
        _ => (settings.Upgrade!.UnsatBuffer, settings.Upgrade.WedgeBuffer, settings.Upgrade.MaxActiveDownloads),
    };

    [Theory]
    [MemberData(nameof(Sections))]
    public void Load_NoneSet_LeavesThemToTheTopLevelSettings(string name, string section)
    {
        Assert.Equal((null, null, null), Budgets(Load(section, ""), name));
    }

    [Theory]
    [MemberData(nameof(Sections))]
    public void Load_AllSet_ReadsThem_WithoutANameNeeded(string name, string section)
    {
        var settings = Load(section, "unsat_buffer = 30\nwedge_buffer = 4\nmax_active_downloads = 5\n");

        Assert.Equal((30, 4, 5), Budgets(settings, name));
    }

    [Theory]
    [MemberData(nameof(Sections))]
    public void Load_OutOfRange_StopsStartup(string name, string section)
    {
        foreach (var (setting, expected) in new[]
        {
            ("unsat_buffer = -1", "'unsat_buffer'"),
            ("wedge_buffer = -1", "'wedge_buffer'"),
            ("max_active_downloads = 0", "'max_active_downloads'"),
        })
        {
            File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + section.Replace("{0}", setting + "\n", StringComparison.Ordinal));
            var error = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

            Assert.Contains(expected, error.Message);
            Assert.Contains(name == "hardcover_list" ? "hardcover_list" : $"[{name}]", error.Message);
        }
    }
}
