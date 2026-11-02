using System.Runtime.CompilerServices;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

/// <summary>
/// Confirms every config under <c>docs/config-examples</c> - shared as reference configs, with
/// intentionally non-functional credentials so they are safe to publish - still parses and passes
/// validation as a TOML-only config file.
/// </summary>
public sealed class ExampleConfigTests
{
    private static string ExamplesDirectory([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", "docs", "config-examples"));

    public static TheoryData<string> ExampleFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(ExamplesDirectory(), "*.toml").Order())
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Fact]
    public void GetFiles_ExamplesDirectory_HasTheNumberedExamples()
    {
        var names = Directory.GetFiles(ExamplesDirectory(), "*.toml").Select(Path.GetFileName).Order().ToList();

        Assert.Equal(5, names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            Assert.StartsWith($"{i + 1}-", names[i]);
        }
    }

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public void Load_Example_ParsesSuccessfully(string fileName)
    {
        var settings = ConfigLoader.Load(Path.Combine(ExamplesDirectory(), fileName));

        Assert.False(string.IsNullOrEmpty(settings.MamId));
        Assert.NotEmpty(settings.QBittorrent);
        Assert.NotEmpty(settings.Library);
    }

    [Fact]
    public void Load_EverythingExample_SetsEveryTopLevelSectionKind()
    {
        var settings = ConfigLoader.Load(Path.Combine(ExamplesDirectory(), "5-everything.toml"));

        Assert.NotEmpty(settings.Autograb);
        Assert.NotEmpty(settings.Snatchlist);
        Assert.NotEmpty(settings.Tag);
        Assert.NotNull(settings.Audiobookshelf);
        Assert.NotNull(settings.Search.WedgeOver);
    }
}
