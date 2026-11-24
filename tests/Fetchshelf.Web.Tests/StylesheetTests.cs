using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// The stylesheets' braces close, and no @media block sits inside another. A lost closing brace once left every rule
/// after a phone-only block inside it, so the menu badges and the rest kept their style only on narrow screens.
/// </summary>
public partial class StylesheetTests
{
    public static TheoryData<string> Stylesheets() => new(
        Directory.GetFiles(Path.Combine(RepositoryRoot(), "src", "Fetchshelf.Web", "wwwroot", "css"), "*.css").Select(Path.GetFileName)!);

    [Theory]
    [MemberData(nameof(Stylesheets))]
    public void Stylesheet_EveryBlockCloses_AndNoMediaBlockIsNested(string file)
    {
        var css = Comment().Replace(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Fetchshelf.Web", "wwwroot", "css", file)), "");
        var lines = css.Split('\n');
        var depth = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("@media", StringComparison.Ordinal))
            {
                Assert.True(depth == 0, $"{file}: an @media block inside another block, near \"{line.Trim()}\"");
            }

            depth += line.Count(c => c == '{') - line.Count(c => c == '}');
            Assert.True(depth >= 0, $"{file}: a closing brace with nothing to close, near \"{line.Trim()}\"");
        }

        Assert.Equal(0, depth);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Fetchshelf.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Fetchshelf.sln not found above the test output");
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();
}
