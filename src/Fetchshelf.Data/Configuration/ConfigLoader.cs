using System.Globalization;
using Tomlyn;
using Tomlyn.Parsing;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// Loads and validates configuration from a TOML file, applying
/// <c>FETCHSHELF_CONF_&lt;setting&gt;</c> environment-variable overrides.
/// </summary>
public static class ConfigLoader
{
    private const string EnvironmentVariablePrefix = "FETCHSHELF_CONF_";

    private static readonly IYamlTypeConverter[] TypeConverters =
    [
        new CategoryListOrBoolYamlConverter(),
        new AutograbTypeYamlConverter(),
        new ConfiguredCostYamlConverter(),
    ];

    private static readonly Dictionary<string, Action<FetchshelfSettings, string>> EnvironmentOverrides = new()
    {
        ["mam_id"] = (s, v) => s.MamId = v,
        ["mam_id_file"] = (s, v) => s.MamIdFile = v,
        ["web_host"] = (s, v) => s.WebHost = v,
        ["web_port"] = (s, v) => s.WebPort = int.Parse(v, CultureInfo.InvariantCulture),
        ["min_ratio"] = (s, v) => s.MinRatio = double.Parse(v, CultureInfo.InvariantCulture),
        ["unsat_buffer"] = (s, v) => s.UnsatBuffer = int.Parse(v, CultureInfo.InvariantCulture),
        ["wedge_buffer"] = (s, v) => s.WedgeBuffer = int.Parse(v, CultureInfo.InvariantCulture),
        ["add_torrents_stopped"] = (s, v) => s.AddTorrentsStopped = bool.Parse(v),
        ["page_explanations"] = (s, v) => s.PageExplanations = bool.Parse(v),
        ["job_explanations"] = (s, v) => s.JobExplanations = bool.Parse(v),
        ["exclude_narrator_in_library_dir"] = (s, v) => s.ExcludeNarratorInLibraryDir = bool.Parse(v),
        ["link_method"] = (s, v) => s.LinkMethod = Enum.Parse<LibraryLinkMethod>(v.Replace("_", "", StringComparison.Ordinal), ignoreCase: true),
        ["search_interval"] = (s, v) => s.SearchInterval = int.Parse(v, CultureInfo.InvariantCulture),
        ["link_interval"] = (s, v) => s.LinkInterval = int.Parse(v, CultureInfo.InvariantCulture),
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
    /// <c>mam_id</c>) if none exists, then applying environment-variable overrides. The path
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
        ResolveMamIdFile(settings);
        ApplyGlobalLinkMethod(settings);
        ValidateQBittorrentConnections(settings);
        ValidateLibraries(settings);
        ValidateAutograbs(settings);
        ValidateSnatchlists(settings);
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

        TomlTableShapeValidator.Validate(model, typeof(FetchshelfSettings), type => TypeConverters.Any(c => c.Accepts(type)));
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
        var builder = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithEnumNamingConvention(UnderscoredNamingConvention.Instance);
        foreach (var converter in TypeConverters)
        {
            builder.WithTypeConverter(converter);
        }

        var deserializer = builder.Build();

        try
        {
            return deserializer.Deserialize<FetchshelfSettings>(yaml) ?? new FetchshelfSettings();
        }
        catch (YamlException ex) when (ex.InnerException is ConfigurationException converterError)
        {
            // A type converter's own error; YamlDotNet wraps it as "Exception during deserialization".
            throw new ConfigurationException(converterError.Message, ex);
        }
        catch (YamlException ex) when (ex.InnerException is FormatException or OverflowException)
        {
            throw new ConfigurationException($"Invalid configuration: {ex.InnerException.Message}", ex);
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

    /// <summary>
    /// Reads <c>mam_id</c> from the file <c>mam_id_file</c> names, if set. Runs after environment overrides so that
    /// a conflict is caught wherever each value came from. Blank counts as unset, as it does for <c>mam_id</c>.
    /// </summary>
    private static void ResolveMamIdFile(FetchshelfSettings settings)
    {
        var path = settings.MamIdFile;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.MamId))
        {
            throw new ConfigurationException("Invalid configuration: set either 'mam_id' or 'mam_id_file', not both.");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ConfigurationException(
                $"Invalid configuration: 'mam_id_file' must be a full path, such as /run/secrets/mam_id; got '{path}'.");
        }

        if (!File.Exists(path))
        {
            throw new ConfigurationException($"Invalid configuration: the 'mam_id_file' '{path}' does not exist.");
        }

        try
        {
            // Secret files usually end with a newline, which is not part of the session id.
            settings.MamId = File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException($"Invalid configuration: the 'mam_id_file' '{path}' could not be read: {ex.Message}", ex);
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

    /// <summary>Gives every library without its own <c>method</c> the global <c>link_method</c>. Runs after environment
    /// overrides, so <c>FETCHSHELF_CONF_link_method</c> reaches those libraries too.</summary>
    private static void ApplyGlobalLinkMethod(FetchshelfSettings settings)
    {
        foreach (var library in settings.Library.Where(l => !l.HasOwnMethod))
        {
            library.Method = settings.LinkMethod;
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

            if (!AutograbSearchOptions.TryParseSortBy(autograb.SortBy, out _))
            {
                throw new ConfigurationException(
                    $"Invalid configuration: an [[autograb]] section has an unrecognized 'sort_by' value '{autograb.SortBy}'; " +
                    $"expected one of {AutograbSearchOptions.SortByNames}.");
            }

            var unknownField = autograb.SearchIn.FirstOrDefault(f => !AutograbSearchOptions.TryParseSearchField(f, out _));
            if (unknownField is not null)
            {
                throw new ConfigurationException(
                    $"Invalid configuration: an [[autograb]] section has an unrecognized 'search_in' field '{unknownField}'; " +
                    $"expected one of {AutograbSearchOptions.SearchInNames}.");
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
}
