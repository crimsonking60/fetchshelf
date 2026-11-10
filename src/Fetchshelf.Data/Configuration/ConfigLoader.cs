using System.Globalization;
using Fetchshelf.Core.Organizer;
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

    private static readonly Dictionary<string, Action<FetchshelfSettings, string, string>> EnvironmentOverrides = new()
    {
        ["mam_id"] = (s, _, v) => s.MamId = v,
        ["mam_id_file"] = (s, _, v) => s.MamIdFile = v,
        ["web_host"] = (s, _, v) => s.WebHost = v,
        ["web_port"] = (s, n, v) => s.WebPort = ParseInt(n, v),
        ["min_ratio"] = (s, n, v) => s.MinRatio = ParseDouble(n, v),
        ["unsat_buffer"] = (s, n, v) => s.UnsatBuffer = ParseInt(n, v),
        ["wedge_buffer"] = (s, n, v) => s.WedgeBuffer = ParseInt(n, v),
        ["add_torrents_stopped"] = (s, n, v) => s.AddTorrentsStopped = ParseBool(n, v),
        ["download_after_selection"] = (s, n, v) => s.DownloadAfterSelection = ParseBool(n, v),
        ["page_explanations"] = (s, n, v) => s.PageExplanations = ParseBool(n, v),
        ["job_explanations"] = (s, n, v) => s.JobExplanations = ParseBool(n, v),
        ["development_banner"] = (s, n, v) => s.DevelopmentBanner = ParseBool(n, v),
        ["exclude_narrator_in_library_dir"] = (s, n, v) => s.ExcludeNarratorInLibraryDir = ParseBool(n, v),
        ["database_wal"] = (s, n, v) => s.DatabaseWal = ParseBool(n, v),
        ["metadata_json"] = (s, n, v) => s.MetadataJson = ParseBool(n, v),
        ["metadata_opf"] = (s, n, v) => s.MetadataOpf = ParseBool(n, v),
        ["link_method"] = (s, n, v) => s.LinkMethod = ParseLinkMethod(n, v),
        ["search_interval"] = (s, n, v) => s.SearchInterval = ParseInt(n, v),
        ["link_interval"] = (s, n, v) => s.LinkInterval = ParseInt(n, v),
        ["ignore_torrents"] = (s, _, v) => s.IgnoreTorrents = SplitList(v),
        ["languages"] = (s, _, v) => s.Languages = SplitList(v),
        ["audio_types"] = (s, _, v) => s.AudioTypes = SplitList(v),
        ["ebook_types"] = (s, _, v) => s.EbookTypes = SplitList(v),
        ["music_types"] = (s, _, v) => s.MusicTypes = SplitList(v),
        ["radio_types"] = (s, _, v) => s.RadioTypes = SplitList(v),
        ["hardcover_token"] = (s, _, v) => (s.Hardcover ??= new()).Token = v,
        ["hardcover_token_file"] = (s, _, v) => (s.Hardcover ??= new()).TokenFile = v,
        ["opds_libraries"] = (s, _, v) => (s.Opds ??= new()).Libraries = SplitList(v),
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
        ConfigValueValidator.ValidateRanges(settings);
        ConfigValueValidator.ValidateNames(settings);
        ResolveMamIdFile(settings);
        ResolveHardcoverTokenFile(settings);
        ApplyGlobalLibraryDefaults(settings);
        ApplyGlobalLanguages(settings);
        ConfigValueValidator.ValidateFilters(settings);
        ValidateQBittorrentConnections(settings);
        ValidateLibraries(settings);
        ValidateAutograbs(settings);
        ValidateSnatchlists(settings);
        ValidateHardcover(settings);
        ValidateOpds(settings);
        ValidateAudiobookshelf(settings);
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
            var variable = EnvironmentVariablePrefix + key;
            var value = Environment.GetEnvironmentVariable(variable);
            if (value is not null)
            {
                setter(settings, variable, value);
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

        settings.MamId = ReadSecretFile("mam_id_file", path, "/run/secrets/mam_id");
    }

    /// <summary>Reads the Hardcover token from the file <c>token_file</c> names, if set, like <c>mam_id_file</c>.</summary>
    private static void ResolveHardcoverTokenFile(FetchshelfSettings settings)
    {
        var path = settings.Hardcover?.TokenFile;
        if (settings.Hardcover is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.Hardcover.Token))
        {
            throw new ConfigurationException("Invalid configuration: set either 'token' or 'token_file' in [hardcover], not both.");
        }

        settings.Hardcover.Token = ReadSecretFile("token_file", path, "/run/secrets/hardcover_token");
    }

    private static string ReadSecretFile(string setting, string path, string example)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ConfigurationException(
                $"Invalid configuration: '{setting}' must be a full path, such as {example}; got '{path}'.");
        }

        if (!File.Exists(path))
        {
            throw new ConfigurationException($"Invalid configuration: the '{setting}' '{path}' does not exist.");
        }

        try
        {
            // Secret files usually end with a newline, which is not part of the secret.
            return File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException($"Invalid configuration: the '{setting}' '{path}' could not be read: {ex.Message}", ex);
        }
    }

    private static int ParseInt(string variable, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw InvalidOverride(variable, value, "a whole number");

    private static double ParseDouble(string variable, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw InvalidOverride(variable, value, "a number such as 1.5");

    private static bool ParseBool(string variable, string value) =>
        bool.TryParse(value, out var result) ? result : throw InvalidOverride(variable, value, "true or false");

    private static LibraryLinkMethod ParseLinkMethod(string variable, string value) =>
        // Enum.TryParse also takes "2" or "1, 2"; only the names count.
        Enum.GetNames<LibraryLinkMethod>().Contains(value.Replace("_", "", StringComparison.Ordinal), StringComparer.OrdinalIgnoreCase)
            && Enum.TryParse<LibraryLinkMethod>(value.Replace("_", "", StringComparison.Ordinal), ignoreCase: true, out var result)
            ? result
            : throw InvalidOverride(variable, value, $"one of {LinkMethodNames}");

    private static string LinkMethodNames => string.Join(", ", Enum.GetValues<LibraryLinkMethod>()
        .Select(method => UnderscoredNamingConvention.Instance.Apply(method.ToString())));

    private static ConfigurationException InvalidOverride(string variable, string value, string expected) =>
        new($"Invalid configuration: {variable} is '{value}'; expected {expected}.");

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

    /// <summary>Gives every library without its own <c>method</c>, <c>metadata_json</c> or <c>metadata_opf</c> the global
    /// <c>link_method</c>, <c>metadata_json</c> or <c>metadata_opf</c>. Runs after environment overrides, so
    /// <c>FETCHSHELF_CONF_link_method</c> and the others reach those libraries too.</summary>
    private static void ApplyGlobalLibraryDefaults(FetchshelfSettings settings)
    {
        foreach (var library in settings.Library)
        {
            if (!library.HasOwnMethod)
            {
                library.Method = settings.LinkMethod;
            }

            if (!library.HasOwnMetadataJson)
            {
                library.MetadataJson = settings.MetadataJson;
            }

            if (!library.HasOwnMetadataOpf)
            {
                library.MetadataOpf = settings.MetadataOpf;
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

            // Its queued torrents are matched to it by name when they are downloaded.
            if ((autograb.UnsatBuffer is not null || autograb.WedgeBuffer is not null) && string.IsNullOrWhiteSpace(autograb.Name))
            {
                throw new ConfigurationException(
                    "Invalid configuration: an [[autograb]] section setting 'unsat_buffer' or 'wedge_buffer' must also set 'name'.");
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

    /// <summary>
    /// Checks the top-level <c>languages</c> and gives it to every <c>[[autograb]]</c> and <c>[[hardcover_list.grab]]</c>
    /// block that doesn't set its own. <c>[[tag]]</c> rules are left alone on purpose: they sort out what was selected,
    /// so a default language there would stop a rule matching a book a block explicitly allowed in another language.
    /// <c>[[snatchlist]]</c> blocks don't take <c>languages</c> at all. Runs after environment overrides, so
    /// <c>FETCHSHELF_CONF_languages</c> reaches the blocks too.
    /// </summary>
    private static void ApplyGlobalLanguages(FetchshelfSettings settings)
    {
        var unknown = settings.Languages.FirstOrDefault(name => !Core.Metadata.LanguageNameResolver.TryResolve(name, out _));
        if (unknown is not null)
        {
            throw new ConfigurationException($"Invalid configuration: unrecognized language '{unknown}' in the top-level 'languages'.");
        }

        var blocks = settings.Autograb.Cast<SharedFilterConfig>().Concat(settings.HardcoverList.SelectMany(list => list.Grab));
        foreach (var block in blocks.Where(block => block.Languages is null))
        {
            block.Languages = [.. settings.Languages];
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

            if (snatchlist.Languages is { Count: > 0 })
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

    private static void ValidateOpds(FetchshelfSettings settings)
    {
        if (settings.Opds is not { } opds)
        {
            return;
        }

        var names = settings.Library.Select(library => library.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var unknown = opds.Libraries.FirstOrDefault(name => !names.Contains(name));
        if (unknown is not null)
        {
            throw new ConfigurationException(
                $"Invalid configuration: [opds] 'libraries' names '{unknown}', but no [[library]] has that name.");
        }
    }

    private static void ValidateAudiobookshelf(FetchshelfSettings settings)
    {
        if (settings.Audiobookshelf is not { PathMapping.Count: > 0 } abs)
        {
            return;
        }

        var shared = abs.PathMapping.GroupBy(pair => pair.Value, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (shared is not null)
        {
            throw new ConfigurationException(
                $"Invalid configuration: [audiobookshelf.path_mapping] maps more than one Audiobookshelf path to '{shared.Key}'.");
        }

        // 2026.11.5 read the table the other way round. A table whose Fetchshelf side has nothing to do with any
        // library_dir while its Audiobookshelf side does was almost certainly written in that order.
        var libraryDirs = settings.Library.Select(library => library.LibraryDir).Where(dir => dir.Length > 0).ToList();
        bool Related(string path) => libraryDirs.Any(dir => PathPrefix.IsSameOrUnder(path, dir) || PathPrefix.IsSameOrUnder(dir, path));
        if (libraryDirs.Count > 0 && !abs.PathMapping.Values.Any(Related) && abs.PathMapping.Keys.Any(Related))
        {
            throw new ConfigurationException(
                "Invalid configuration: [audiobookshelf.path_mapping] looks like it is written Fetchshelf's path = "
                + "Audiobookshelf's path. It is the other way round, like [qbittorrent.path_mapping]: Audiobookshelf's "
                + "path = Fetchshelf's path. Swap the two sides of each line.");
        }
    }

    private static void ValidateHardcover(FetchshelfSettings settings)
    {
        if (settings.Hardcover is { } hardcover && string.IsNullOrWhiteSpace(hardcover.Token))
        {
            throw new ConfigurationException("Invalid configuration: the [hardcover] section needs 'token' or 'token_file'.");
        }

        foreach (var list in settings.HardcoverList)
        {
            var label = string.IsNullOrWhiteSpace(list.Name) ? "a [[hardcover_list]] section" : $"the [[hardcover_list]] '{list.Name}'";

            if (settings.Hardcover is null)
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {label} needs a [hardcover] section with the token for Hardcover's API.");
            }

            var hasList = !string.IsNullOrWhiteSpace(list.List);
            if (list.Status is null == !hasList)
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {label} must set one of 'status' or 'list', not both.");
            }

            if (list.Grab.Count == 0)
            {
                throw new ConfigurationException($"Invalid configuration: {label} must have at least one 'grab' block.");
            }

            if (list.PreferFormat is not (null or Core.Metadata.FilterMainType.Audio or Core.Metadata.FilterMainType.Ebook))
            {
                throw new ConfigurationException($"Invalid configuration: {label} has a 'prefer_format' that is not 'audio' or 'ebook'.");
            }

            if (list.Grab.Any(g => g.Cost is Core.AutoDownloader.ConfiguredCost.MetadataOnly or Core.AutoDownloader.ConfiguredCost.MetadataOnlyAdd))
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {label} has a 'grab' block with a metadata-only 'cost'; Hardcover lists download books.");
            }
        }
    }
}
