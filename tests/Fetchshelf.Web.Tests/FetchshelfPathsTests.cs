using Fetchshelf.Web.Hosting;

namespace Fetchshelf.Web.Tests;

public sealed class FetchshelfPathsTests
{
    private static FetchshelfPaths Resolve(Dictionary<string, string> environment, string? defaultDirectory = null) =>
        FetchshelfPaths.Resolve(name => environment.GetValueOrDefault(name), defaultDirectory);

    [Fact]
    public void Resolve_NothingSet_UsesTheWorkingDirectoryDefaults()
    {
        Assert.Equal(new FetchshelfPaths("config.toml", "data.db", null), Resolve([]));
    }

    [Fact]
    public void Resolve_EverythingSet_UsesTheVariables()
    {
        var paths = Resolve(new()
        {
            ["FETCHSHELF_CONFIG_FILE"] = "/config/config.toml",
            ["FETCHSHELF_DATA_FILE"] = "/database/data.db",
            ["FETCHSHELF_LOG_DIR"] = "/config/logs",
        });

        Assert.Equal(new FetchshelfPaths("/config/config.toml", "/database/data.db", "/config/logs"), paths);
    }

    [Theory]
    [InlineData("FETCHSHELF_CONFIG_FILE", "/c/config.toml", "data.db", null)]
    [InlineData("FETCHSHELF_DATA_FILE", "config.toml", "/d/data.db", null)]
    [InlineData("FETCHSHELF_LOG_DIR", "config.toml", "data.db", "/l")]
    public void Resolve_OneSet_LeavesTheOthersAtTheirDefaults(string variable, string configFile, string dataFile, string? logFolder)
    {
        var value = variable switch
        {
            "FETCHSHELF_CONFIG_FILE" => "/c/config.toml",
            "FETCHSHELF_DATA_FILE" => "/d/data.db",
            _ => "/l",
        };

        Assert.Equal(new FetchshelfPaths(configFile, dataFile, logFolder), Resolve(new() { [variable] = value }));
    }

    [Fact]
    public void Resolve_EmptyLogFolder_IsKept_SoNoLogFileIsWritten()
    {
        Assert.Equal("", Resolve(new() { ["FETCHSHELF_LOG_DIR"] = "" }).LogFolder);
    }

    [Fact]
    public void Resolve_DefaultDirectory_HoldsTheConfigAndTheDatabase()
    {
        var paths = Resolve([], "/home/user/Fetchshelf");

        Assert.Equal((Path.Combine("/home/user/Fetchshelf", "config.toml"), Path.Combine("/home/user/Fetchshelf", "data.db")), (paths.ConfigFile, paths.DataFile));
    }

    [Fact]
    public void DataFolder_IsTheDatabasesFullFolder()
    {
        Assert.Equal("/database", new FetchshelfPaths("c", "/database/data.db", null).DataFolder);
        Assert.Equal(Directory.GetCurrentDirectory(), new FetchshelfPaths("c", "data.db", null).DataFolder);
    }
}
