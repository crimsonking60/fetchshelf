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
}
