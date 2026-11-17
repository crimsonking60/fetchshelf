using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class TagRuleConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-tag-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_TagRuleWithOnlyTags_HasNoCategory()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[tag]]
            tags = ["fiction", "new"]
            """);

        var rule = Assert.Single(ConfigLoader.Load(ConfigPath).Tag);

        Assert.Null(rule.Category);
        Assert.Equal(["fiction", "new"], rule.Tags);
    }

    [Theory]
    [InlineData("[[tag]]\nmedia_type = \"ComicBook\"\ncategory = \"comics\"\n", "media_type")]
    [InlineData("audio_types = \"m4b\"\n", "audio_types")]
    public void Load_StringWhereAListGoes_NamesTheSetting(string toml, string setting)
    {
        File.WriteAllText(ConfigPath, "mam_id = \"\"\n" + toml);

        var error = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Equal($"Invalid configuration: '{setting}' must be a list: write {setting} = [\"...\"], not {setting} = \"...\".", error.Message);
    }
}
