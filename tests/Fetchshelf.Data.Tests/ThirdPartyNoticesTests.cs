using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Fetchshelf.Data.Tests;

/// <summary>
/// Every package the application ships is listed in THIRD-PARTY-NOTICES.md, with its license: a fork that redistributes
/// Fetchshelf keeps that file, so a package missing from it would be redistributed without its notice.
/// </summary>
public sealed class ThirdPartyNoticesTests
{
    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    [Fact]
    public void EveryPackageReferencedUnderSrc_IsInTheNotices()
    {
        var root = RepositoryRoot();
        var notices = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));

        // A package with PrivateAssets="all", such as Microsoft.EntityFrameworkCore.Design, is used only to build.
        var shipped = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(project => XDocument.Load(project).Descendants("PackageReference"))
            .Where(reference => (string?)reference.Attribute("PrivateAssets") != "all" && (string?)reference.Element("PrivateAssets") != "all")
            .Select(reference => (string)reference.Attribute("Include")!)
            .Distinct()
            .ToList();

        Assert.NotEmpty(shipped);
        Assert.All(shipped, package => Assert.Contains(package, notices, StringComparison.Ordinal));
    }
}
