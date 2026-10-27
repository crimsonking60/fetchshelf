using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class AudiobookshelfConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-abs-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_NoAudiobookshelfSection_IsNull()
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n");

        var settings = ConfigLoader.Load(ConfigPath);

        Assert.Null(settings.Audiobookshelf);
    }

    [Fact]
    public void Load_AudiobookshelfSectionWithoutInterval_DefaultsToTen()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [audiobookshelf]
            url = "http://abs.local"
            token = "secret-token"
            """);

        var abs = ConfigLoader.Load(ConfigPath).Audiobookshelf;

        Assert.NotNull(abs);
        Assert.Equal("http://abs.local", abs!.Url);
        Assert.Equal("secret-token", abs.Token);
        Assert.Equal(10, abs.Interval);
    }
}
