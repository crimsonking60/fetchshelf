using System.Globalization;
using Tomlyn;
using Tomlyn.Parsing;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// Loads and validates Fetchshelf-spec.md §4 configuration from a TOML file, applying
/// <c>FETCHSHELF_CONF_&lt;setting&gt;</c> environment-variable overrides.
/// </summary>
public static class ConfigLoader
{
    private const string EnvironmentVariablePrefix = "FETCHSHELF_CONF_";

    private static readonly Dictionary<string, Action<FetchshelfSettings, string>> EnvironmentOverrides = new()
    {
        ["mam_id"] = (s, v) => s.MamId = v,
        ["web_host"] = (s, v) => s.WebHost = v,
        ["web_port"] = (s, v) => s.WebPort = int.Parse(v, CultureInfo.InvariantCulture),
        ["min_ratio"] = (s, v) => s.MinRatio = double.Parse(v, CultureInfo.InvariantCulture),
        ["unsat_buffer"] = (s, v) => s.UnsatBuffer = int.Parse(v, CultureInfo.InvariantCulture),
        ["wedge_buffer"] = (s, v) => s.WedgeBuffer = int.Parse(v, CultureInfo.InvariantCulture),
        ["add_torrents_stopped"] = (s, v) => s.AddTorrentsStopped = bool.Parse(v),
        ["page_explanations"] = (s, v) => s.PageExplanations = bool.Parse(v),
        ["job_explanations"] = (s, v) => s.JobExplanations = bool.Parse(v),
        ["exclude_narrator_in_library_dir"] = (s, v) => s.ExcludeNarratorInLibraryDir = bool.Parse(v),
        ["search_interval"] = (s, v) => s.SearchInterval = int.Parse(v, CultureInfo.InvariantCulture),
        ["link_interval"] = (s, v) => s.LinkInterval = int.Parse(v, CultureInfo.InvariantCulture),
        ["import_interval"] = (s, v) => s.ImportInterval = int.Parse(v, CultureInfo.InvariantCulture),
        ["ignore_torrents"] = (s, v) => s.IgnoreTorrents = SplitList(v),
        ["audio_types"] = (s, v) => s.AudioTypes = SplitList(v),
        ["ebook_types"] = (s, v) => s.EbookTypes = SplitList(v),
        ["music_types"] = (s, v) => s.MusicTypes = SplitList(v),
        ["radio_types"] = (s, v) => s.RadioTypes = SplitList(v),
    };

    /// <summary>
    /// Resolves the config file path to use when none is explicitly configured: always
    /// <c>config.toml</c>, in <paramref name="directory"/> if given, otherwise the current directory.
    /// </summary>
    public static string ResolveDefaultConfigFilePath(string? directory = null) =>
        directory is null ? "config.toml" : Path.Combine(directory, "config.toml");

    /// <summary>
    /// Loads settings from <paramref name="configFilePath"/>, creating a minimal file (empty
    /// <c>mam_id</c>) if none exists (§4), then applying environment-variable overrides. The path
    /// must have a <c>.toml</c> extension.
    /// </summary>
    public static FetchshelfSettings Load(string configFilePath)
    {
        DetermineFormat(configFilePath);
        EnsureConfigFileExists(configFilePath);
        var text = File.ReadAllText(configFilePath);
        var yaml = ConvertTomlToYaml(text);
        var settings = Deserialize(yaml);
        ApplyEnvironmentOverrides(settings);
        ValidateQBittorrentConnections(settings);
        ValidateLibraries(settings);
        ValidateAutograbs(settings);
        ValidateSnatchlists(settings);
        ValidateNotionLists(settings);
        return settings;
    }

    private static void DetermineFormat(string configFilePath)
    {
        var extension = Path.GetExtension(configFilePath);
        if (!string.Equals(extension, ".toml", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException(
                $"Invalid configuration: unrecognized config file extension '{extension}'; expected '.toml'.");
        }
    }

    private static void EnsureConfigFileExists(string configFilePath)
    {
        if (File.Exists(configFilePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(configFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(configFilePath, "mam_id = \"\"\n");
    }

    /// <summary>
    /// Parses TOML into plain dictionaries/lists/scalars and re-serializes that as YAML text so it
    /// can flow through the same deserialization pipeline (naming convention, custom converters,
    /// strict unknown-key rejection) as a native YAML config.
    /// </summary>
    private static string ConvertTomlToYaml(string toml)
    {
        Dictionary<object, object?> model;
        try
        {
            model = ReadTomlDocument(toml);
        }
        catch (TomlException ex)
        {
            throw new ConfigurationException($"Invalid configuration: {ex.Message}", ex);
        }

        return new SerializerBuilder().Build().Serialize(model);
    }

    /// <summary>
    /// Builds the document from Tomlyn's parse-event stream rather than <c>TomlTable</c> binding:
    /// Tomlyn 2.10 keeps only the last <c>[[x]]</c> group when the same array of tables is split
    /// around another table (<c>[[tag]]</c>, <c>[[library]]</c>, <c>[[tag]]</c>, ...), silently
    /// dropping the earlier entries.
    /// </summary>
    private static Dictionary<object, object?> ReadTomlDocument(string toml)
    {
        var parser = TomlParser.Create(toml, new TomlParserOptions { DecodeScalars = true });
        parser.MoveNext(); // StartDocument
        parser.MoveNext(); // StartTable (root)
        return ReadTomlTable(parser);
    }

    // Positioned on a table's StartTable event; consumes through its matching EndTable.
    private static Dictionary<object, object?> ReadTomlTable(TomlParser parser)
    {
        var table = new Dictionary<object, object?>();
        while (parser.MoveNext() && parser.Current.Kind != TomlParseEventKind.EndTable)
        {
            var key = parser.Current.PropertyName!;
            parser.MoveNext();
            var value = ReadTomlValue(parser);

            if (!table.TryGetValue(key, out var existing))
            {
                table[key] = value;
            }
            else if (existing is List<object?> earlier && value is List<object?> later
                && earlier.All(e => e is Dictionary<object, object?>) && later.All(e => e is Dictionary<object, object?>))
            {
                earlier.AddRange(later);
            }
            else
            {
                throw new TomlException($"Duplicate key '{key}' was encountered.");
            }
        }

        return table;
    }

    // Positioned on the first event of a value; consumes through the value's last event.
    private static object? ReadTomlValue(TomlParser parser)
    {
        var current = parser.Current;
        switch (current.Kind)
        {
            case TomlParseEventKind.StartTable:
                return ReadTomlTable(parser);
            case TomlParseEventKind.StartArray:
                var items = new List<object?>();
                while (parser.MoveNext() && parser.Current.Kind != TomlParseEventKind.EndArray)
                {
                    items.Add(ReadTomlValue(parser));
                }

                return items;
            case TomlParseEventKind.String:
                return current.GetString();
            case TomlParseEventKind.Integer:
                return current.GetInt64();
            case TomlParseEventKind.Float:
                return current.GetDouble();
            case TomlParseEventKind.Boolean:
                return current.GetBoolean();
            case TomlParseEventKind.DateTime:
                return current.GetTomlDateTime().ToString();
            default:
                throw new TomlException($"Unexpected TOML parse event '{current.Kind}'.");
        }
    }

    private static FetchshelfSettings Deserialize(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithEnumNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new CategoryListOrBoolYamlConverter())
            .WithTypeConverter(new AutogrebTypeYamlConverter())
            .WithTypeConverter(new ConfiguredCostYamlConverter())
            .Build();

        try
        {
            return deserializer.Deserialize<FetchshelfSettings>(yaml) ?? new FetchshelfSettings();
        }
        catch (YamlException ex)
        {
            throw new ConfigurationException($"Invalid configuration: {ex.Message}", ex);
        }
    }

    private static void ApplyEnvironmentOverrides(FetchshelfSettings settings)
    {
        foreach (var (key, setter) in EnvironmentOverrides)
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariablePrefix + key);
            if (value is not null)
            {
                setter(settings, value);
            }
        }
    }

    private static List<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    private static void ValidateQBittorrentConnections(FetchshelfSettings settings)
    {
        foreach (var connection in settings.QBittorrent)
        {
            if (string.IsNullOrWhiteSpace(connection.Url))
            {
                throw new ConfigurationException("Invalid configuration: a [[qbittorrent]] section is missing 'url'.");
            }

            if (string.IsNullOrWhiteSpace(connection.Username))
            {
                throw new ConfigurationException("Invalid configuration: a [[qbittorrent]] section is missing 'username'.");
            }

            if (string.IsNullOrWhiteSpace(connection.Password))
            {
                throw new ConfigurationException("Invalid configuration: a [[qbittorrent]] section is missing 'password'.");
            }
        }
    }

    private static void ValidateLibraries(FetchshelfSettings settings)
    {
        foreach (var library in settings.Library)
        {
            var hasCategory = !string.IsNullOrWhiteSpace(library.Category);
            var hasDownloadDir = !string.IsNullOrWhiteSpace(library.DownloadDir);

            if (hasCategory && hasDownloadDir)
            {
                throw new ConfigurationException(
                    "Invalid configuration: a [[library]] section must specify only one of 'category' or 'download_dir', not both.");
            }

            if (!hasCategory && !hasDownloadDir)
            {
                throw new ConfigurationException(
                    "Invalid configuration: a [[library]] section must specify one of 'category' or 'download_dir'.");
            }
        }
    }

    private static void ValidateAutograbs(FetchshelfSettings settings)
    {
        foreach (var autograb in settings.Autograb)
        {
            if (autograb.Type is null)
            {
                throw new ConfigurationException("Invalid configuration: an [[autograb]] section is missing 'type'.");
            }

            if (autograb.MaxActiveDownloads is not null && string.IsNullOrWhiteSpace(autograb.Name))
            {
                throw new ConfigurationException(
                    "Invalid configuration: an [[autograb]] section setting 'max_active_downloads' must also set 'name'.");
            }
        }
    }

    private static void ValidateSnatchlists(FetchshelfSettings settings)
    {
        foreach (var snatchlist in settings.Snatchlist)
        {
            if (snatchlist.Cost is not (Core.AutoDownloader.ConfiguredCost.MetadataOnly or Core.AutoDownloader.ConfiguredCost.MetadataOnlyAdd))
            {
                throw new ConfigurationException(
                    "Invalid configuration: a [[snatchlist]] section's 'cost' must be 'metadata_only' or 'metadata_only_add'.");
            }

            if (snatchlist.Languages.Count > 0)
            {
                throw new ConfigurationException(
                    "Invalid configuration: a [[snatchlist]] section must not set 'languages'.");
            }

            if (snatchlist.UploadedAfter is not null || snatchlist.UploadedBefore is not null)
            {
                throw new ConfigurationException(
                    "Invalid configuration: a [[snatchlist]] section must not set 'uploaded_after' or 'uploaded_before'.");
            }
        }
    }

    private static void ValidateNotionLists(FetchshelfSettings settings)
    {
        foreach (var list in settings.NotionList)
        {
            if (string.IsNullOrWhiteSpace(list.DataSource))
            {
                throw new ConfigurationException("Invalid configuration: a [[notion_list]] section is missing 'data_source'.");
            }

            if (string.IsNullOrWhiteSpace(list.Token))
            {
                throw new ConfigurationException("Invalid configuration: a [[notion_list]] section is missing 'token'.");
            }

            if (string.IsNullOrWhiteSpace(list.Name))
            {
                throw new ConfigurationException("Invalid configuration: a [[notion_list]] section is missing 'name'.");
            }

            if (list.MamFields.Count == 0)
            {
                throw new ConfigurationException("Invalid configuration: a [[notion_list]] section must have at least one 'mam_fields' entry.");
            }

            if (list.Grab.Count == 0)
            {
                throw new ConfigurationException("Invalid configuration: a [[notion_list]] section must have at least one 'grab' block.");
            }
        }
    }
}
