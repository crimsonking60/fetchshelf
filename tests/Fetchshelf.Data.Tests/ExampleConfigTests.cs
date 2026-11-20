using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fetchshelf.Data.Configuration;
using Tomlyn.Parsing;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
        Assert.NotEmpty(settings.Tag);
        Assert.NotNull(settings.Audiobookshelf);
        Assert.NotNull(settings.Search.WedgeOver);
        Assert.Equal(2, settings.Notify.Count);
        Assert.NotNull(settings.WebUrl);
        Assert.NotNull(settings.Backup);
    }

    /// <summary>
    /// Settings the everything example can't set, each shown there in a comment instead.
    /// </summary>
    private static readonly HashSet<string> SettingsNotInEverythingExample =
    [
        "development_banner",      // undocumented, for maintainers
        "mam_id_file",             // excludes mam_id
        "hardcover.token_file",    // excludes token
        "opds.password_file",      // excludes password
        "notify.url_file",         // excludes url
        "file_list.shelf",         // only for an export holding every shelf, not the example's text list
        "bookmarks_list",          // can't be configured beside a type = "bookmarks" autograb
    ];

    /// <summary>Classes read from a single value by their own YAML converter rather than as a table.</summary>
    private static readonly HashSet<Type> ValueTypes = [typeof(CategoryListOrBool), typeof(AutograbType)];

    [Fact]
    public void EverythingExample_SetsEverySetting()
    {
        var toml = File.ReadAllText(Path.Combine(ExamplesDirectory(), "5-everything.toml"));
        var used = new HashSet<string>(StringComparer.Ordinal);
        var parser = TomlParser.Create(toml, new TomlParserOptions { DecodeScalars = true });
        parser.MoveNext(); // StartDocument
        parser.MoveNext(); // StartTable (root)
        CollectKeys(parser, prefix: null, used);

        var missing = SettingPaths(typeof(FetchshelfSettings), prefix: null, inheritFilter: false)
            .Where(path => !used.Contains(path) && !SettingsNotInEverythingExample.Any(skip => path == skip || path.StartsWith(skip + ".", StringComparison.Ordinal)))
            .ToList();

        Assert.True(missing.Count == 0, $"5-everything.toml doesn't set: {string.Join(", ", missing)}");
    }

    // Positioned on a table's StartTable event; consumes through its matching EndTable. Entries of an array of
    // tables share their key's path.
    private static void CollectKeys(TomlParser parser, string? prefix, HashSet<string> used)
    {
        while (parser.MoveNext() && parser.Current.Kind != TomlParseEventKind.EndTable)
        {
            var path = prefix is null ? parser.Current.PropertyName! : $"{prefix}.{parser.Current.PropertyName}";
            used.Add(path);
            parser.MoveNext();
            CollectValue(parser, path, used);
        }
    }

    private static void CollectValue(TomlParser parser, string path, HashSet<string> used)
    {
        switch (parser.Current.Kind)
        {
            case TomlParseEventKind.StartTable:
                CollectKeys(parser, path, used);
                break;
            case TomlParseEventKind.StartArray:
                while (parser.MoveNext() && parser.Current.Kind != TomlParseEventKind.EndArray)
                {
                    CollectValue(parser, path, used);
                }

                break;
        }
    }

    /// <summary>
    /// Every setting's dotted path, named as the loader names it. The shared torrent filter's fields are expected on
    /// [[autograb]] only; the other blocks that take the filter are covered by it.
    /// </summary>
    private static IEnumerable<string> SettingPaths(Type type, string? prefix, bool inheritFilter)
    {
        var flags = BindingFlags.Public | BindingFlags.Instance;
        if (!inheritFilter && typeof(SharedFilterConfig).IsAssignableFrom(type))
        {
            flags |= BindingFlags.DeclaredOnly;
        }

        foreach (var property in type.GetProperties(flags).Where(p => p.CanWrite && p.GetCustomAttribute<YamlIgnoreAttribute>() is null))
        {
            var name = property.GetCustomAttribute<YamlMemberAttribute>()?.Alias ?? UnderscoredNamingConvention.Instance.Apply(property.Name);
            var path = prefix is null ? name : $"{prefix}.{name}";
            yield return path;

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>))
            {
                propertyType = propertyType.GetGenericArguments()[0];
            }

            if (propertyType.IsClass && propertyType != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(propertyType)
                && !ValueTypes.Contains(propertyType))
            {
                foreach (var nested in SettingPaths(propertyType, path, inheritFilter: path == "autograb"))
                {
                    yield return nested;
                }
            }
        }
    }

    [Fact]
    public void ExampleListFile_ReadsEveryBook()
    {
        var settings = ConfigLoader.Load(Path.Combine(ExamplesDirectory(), "5-everything.toml"));

        var contents = Core.Lists.ListFileReader.Read(File.ReadAllText(Assert.Single(settings.FileList).FullPath));

        Assert.Equal(4, contents.Books.Count);
        Assert.Empty(contents.Problems);
    }
}
