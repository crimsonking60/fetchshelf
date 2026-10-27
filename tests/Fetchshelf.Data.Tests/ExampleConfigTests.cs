using System.Runtime.CompilerServices;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>
/// Confirms the repository's root-level <c>example-config.toml</c> — shared as a reference config,
/// with an intentionally non-functional <c>mam_id</c> so it is safe to publish — still parses and
/// passes validation as a TOML-only config file.
/// </summary>
public sealed class ExampleConfigTests
{
    private static string RepoRootExampleConfigPath([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", "example-config.toml");

    [Fact]
    public void Load_ExampleConfigToml_ParsesSuccessfully()
    {
        var path = RepoRootExampleConfigPath();
        Assert.True(File.Exists(path), $"Expected to find example-config.toml at '{path}'.");

        var settings = ConfigLoader.Load(path);

        Assert.False(string.IsNullOrEmpty(settings.MamId));
        Assert.NotEmpty(settings.QBittorrent);
        Assert.NotEmpty(settings.Library);
        Assert.NotEmpty(settings.Autograb);
        Assert.NotEmpty(settings.GoodreadsList);
    }
}
