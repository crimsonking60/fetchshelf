using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class MamIdFileConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-mam-id-file-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    private string SecretPath => Path.Combine(_tempDir, "mam_id");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private void WriteConfig(string toml) => File.WriteAllText(ConfigPath, toml);

    private static string TomlString(string value) => value.Replace("\\", "\\\\");

    [Fact]
    public void Load_MamIdFile_ReadsTheSessionIdAndTrimsTheTrailingNewline()
    {
        File.WriteAllText(SecretPath, "secret-session-id\n");
        WriteConfig($"mam_id_file = \"{TomlString(SecretPath)}\"\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal("secret-session-id", settings.MamId);
        Assert.Equal(SecretPath, settings.MamIdFile);
        Assert.True(settings.BackgroundJobsEnabled);
    }

    [Fact]
    public void Load_MamIdAndMamIdFileBothSet_Throws()
    {
        File.WriteAllText(SecretPath, "from-file");
        WriteConfig($"""
            mam_id = "from-config"
            mam_id_file = "{TomlString(SecretPath)}"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("either 'mam_id' or 'mam_id_file', not both", exception.Message);
    }

    [Fact]
    public void Load_EmptyMamIdWithMamIdFile_UsesTheFile()
    {
        // The config file Fetchshelf creates on first start has mam_id = "", which must not count as set.
        File.WriteAllText(SecretPath, "from-file");
        WriteConfig($"""
            mam_id = ""
            mam_id_file = "{TomlString(SecretPath)}"
            """);

        Assert.Equal("from-file", ConfigLoader.Load(ConfigPath).MamId);
    }

    [Fact]
    public void Load_MamIdFileRelativePath_Throws()
    {
        WriteConfig("mam_id_file = \"secrets/mam_id\"\n");

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("full path", exception.Message);
        Assert.Contains("secrets/mam_id", exception.Message);
    }

    [Fact]
    public void Load_MamIdFileMissing_ThrowsNamingThePath()
    {
        WriteConfig($"mam_id_file = \"{TomlString(SecretPath)}\"\n");

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains(SecretPath, exception.Message);
        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public void Load_EmptyMamIdFile_BehavesLikeAnEmptyMamId()
    {
        File.WriteAllText(SecretPath, "\n");
        WriteConfig($"mam_id_file = \"{TomlString(SecretPath)}\"\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Equal(string.Empty, settings.MamId);
        Assert.False(settings.BackgroundJobsEnabled);
    }
}
