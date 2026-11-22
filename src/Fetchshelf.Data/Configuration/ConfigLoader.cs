using System.Globalization;
using System.Text.RegularExpressions;
using Fetchshelf.Core.Metadata;
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
public static partial class ConfigLoader
{
    private const string EnvironmentVariablePrefix = "FETCHSHELF_CONF_";

    private static readonly IYamlTypeConverter[] TypeConverters =
    [
        // First: YamlDotNet puts each converter on top of those before it, so an enum with a converter of its own
        // below keeps it.
        new EnumYamlConverter(),
        new CategoryListOrBoolYamlConverter(),
        new AutograbTypeYamlConverter(),
        new ConfiguredCostYamlConverter(),
    ];

    private static readonly Dictionary<string, Action<FetchshelfSettings, string, string>> EnvironmentOverrides = new()
    {
        ["mam_id"] = (s, _, v) => s.MamId = v,
        ["mam_id_file"] = (s, _, v) => s.MamIdFile = v,
        ["web_host"] = (s, _, v) => s.WebHost = v,
        ["web_url"] = (s, _, v) => s.WebUrl = v,
        ["web_port"] = (s, n, v) => s.WebPort = ParseInt(n, v),
        ["min_ratio"] = (s, n, v) => s.MinRatio = ParseDouble(n, v),
        ["unsat_buffer"] = (s, n, v) => s.UnsatBuffer = ParseInt(n, v),
        ["wedge_buffer"] = (s, n, v) => s.WedgeBuffer = ParseInt(n, v),
        ["add_torrents_stopped"] = (s, n, v) => s.AddTorrentsStopped = ParseBool(n, v),
        ["download_after_selection"] = (s, n, v) => s.DownloadAfterSelection = ParseBool(n, v),
        ["page_explanations"] = (s, n, v) => s.PageExplanations = ParseBool(n, v),
        ["job_explanations"] = (s, n, v) => s.JobExplanations = ParseBool(n, v),
        ["dry_job_buttons"] = (s, n, v) => s.DryJobButtons = ParseBool(n, v),
        ["development_banner"] = (s, n, v) => s.DevelopmentBanner = ParseBool(n, v),
        ["exclude_narrator_in_library_dir"] = (s, n, v) => s.ExcludeNarratorInLibraryDir = ParseBool(n, v),
        ["database_wal"] = (s, n, v) => s.DatabaseWal = ParseBool(n, v),
        ["metadata_json"] = (s, n, v) => s.MetadataJson = ParseBool(n, v),
        ["metadata_opf"] = (s, n, v) => s.MetadataOpf = ParseBool(n, v),
        ["covers_from_mam"] = (s, n, v) => s.CoversFromMam = ParseInt(n, v),
        ["metrics"] = (s, n, v) => s.Metrics = ParseBool(n, v),
        ["split_collections"] = (s, n, v) => s.SplitCollections = ParseBool(n, v),
        ["link_method"] = (s, n, v) => s.LinkMethod = ParseLinkMethod(n, v),
        ["search_interval"] = (s, n, v) => s.SearchInterval = ParseInt(n, v),
        ["link_interval"] = (s, n, v) => s.LinkInterval = ParseInt(n, v),
        ["ignore_torrents"] = (s, _, v) => s.IgnoreTorrents = SplitList(v),
        ["extra_categories"] = (s, _, v) => s.ExtraCategories = SplitList(v),
        ["languages"] = (s, _, v) => s.Languages = SplitList(v),
        ["audio_types"] = (s, _, v) => s.AudioTypes = SplitList(v),
        ["ebook_types"] = (s, _, v) => s.EbookTypes = SplitList(v),
        ["music_types"] = (s, _, v) => s.MusicTypes = SplitList(v),
        ["radio_types"] = (s, _, v) => s.RadioTypes = SplitList(v),
        ["hardcover_token"] = (s, _, v) => (s.Hardcover ??= new()).Token = v,
        ["hardcover_token_file"] = (s, _, v) => (s.Hardcover ??= new()).TokenFile = v,
        ["opds_libraries"] = (s, _, v) => (s.Opds ??= new()).Libraries = SplitList(v),
        ["opds_username"] = (s, _, v) => (s.Opds ??= new()).Username = v,
        ["opds_password"] = (s, _, v) => (s.Opds ??= new()).Password = v,
        ["opds_password_file"] = (s, _, v) => (s.Opds ??= new()).PasswordFile = v,
        ["allowed_hosts"] = (s, _, v) => s.AllowedHosts = SplitList(v),
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
        ResolveOpdsPasswordFile(settings);
        ResolveNotifyUrlFiles(settings);
        ApplyGlobalLibraryDefaults(settings);
        ApplyGlobalLanguages(settings);
        ConfigValueValidator.ValidateFilters(settings);
        ValidateQBittorrentConnections(settings);
        ConfigValueValidator.ValidateDownloadPlacements(settings);
        ValidateLibraries(settings);
        ValidateAutograbs(settings);
        ValidateHardcover(settings);
        ValidateFileLists(settings, configFilePath);
        ValidateBookmarksList(settings);
        ValidateFollow(settings);
        ValidateUpgrade(settings);
        ValidateOpds(settings);
        ValidateAllowedHosts(settings);
        ValidateAudiobookshelf(settings);
        ConfigValueValidator.ValidateNotify(settings);
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

        RejectRemovedSections(model);
        TomlTableShapeValidator.Validate(model, typeof(FetchshelfSettings), type => TypeConverters.Any(c => c.Accepts(type)));
        return new SerializerBuilder().Build().Serialize(model);
    }

    /// <summary>
    /// Sections Fetchshelf had and removed, refused with why, rather than as an unknown setting. <c>[[snatchlist]]</c>
    /// read MAM's snatch lists through an endpoint that isn't among the parts of MAM its API rules authorize automating.
    /// </summary>
    private static void RejectRemovedSections(Dictionary<object, object?> model)
    {
        if (model.ContainsKey("snatchlist"))
        {
            throw new ConfigurationException(
                "Invalid configuration: [[snatchlist]] has been removed, to conform to MAM's rules for automation: the snatch "
                + "lists it read aren't among the parts of MAM its API documentation authorizes automating. Remove the "
                + "[[snatchlist]] sections; the torrents they recorded are kept.");
        }
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
        // YamlDotNet wraps the cause as "Exception during deserialization", once for each section it is nested in.
        catch (YamlException ex) when (Innermost(ex) is ConfigurationException converterError)
        {
            // A type converter's own error.
            throw new ConfigurationException(converterError.Message, ex);
        }
        catch (YamlException ex) when (Innermost(ex) is FormatException or OverflowException)
        {
            throw new ConfigurationException($"Invalid configuration: {Innermost(ex).Message}", ex);
        }
        catch (YamlException ex) when (Innermost(ex) is InvalidCastException cast)
        {
            throw new ConfigurationException(WrongType(yaml, ex, cast), ex);
        }
        catch (YamlException ex)
        {
            throw new ConfigurationException($"Invalid configuration: {ex.Message}", ex);
        }
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is { } inner)
        {
            ex = inner;
        }

        return ex;
    }

    /// <summary>
    /// Names the setting whose value has the wrong type, such as <c>media_type = "ComicBook"</c> for a list, from the line
    /// of the YAML text YamlDotNet stopped at. The value itself is left out: it could be a secret.
    /// </summary>
    private static string WrongType(string yaml, YamlException ex, InvalidCastException cast)
    {
        var deepest = ex;
        while (deepest.InnerException is YamlException inner)
        {
            deepest = inner;
        }

        var lines = yaml.Split('\n');
        var index = (int)deepest.Start.Line - 1;
        var match = index >= 0 && index < lines.Length
            ? Regex.Match(lines[index], @"^\s*(?:-\s+)?([A-Za-z0-9_]+):")
            : Match.Empty;
        if (!match.Success)
        {
            return $"Invalid configuration: a setting has a value of the wrong type ({cast.Message}).";
        }

        var key = match.Groups[1].Value;
        return cast.Message.Contains("List`1", StringComparison.Ordinal)
            ? $"Invalid configuration: '{key}' must be a list: write {key} = [\"...\"], not {key} = \"...\"."
            : $"Invalid configuration: '{key}' has a value of the wrong type.";
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

    /// <summary>Reads each `[[notify]]` URL from the file <c>url_file</c> names, if set, like <c>mam_id_file</c>.</summary>
    private static void ResolveNotifyUrlFiles(FetchshelfSettings settings)
    {
        for (var i = 0; i < settings.Notify.Count; i++)
        {
            var entry = settings.Notify[i];
            if (string.IsNullOrWhiteSpace(entry.UrlFile))
            {
                continue;
            }

            var label = ConfigValueValidator.Label("notify", entry.Name, i);
            if (!string.IsNullOrWhiteSpace(entry.Url))
            {
                throw new ConfigurationException($"Invalid configuration: {label} sets both 'url' and 'url_file'; set one.");
            }

            entry.Url = ReadSecretFile("url_file", entry.UrlFile, "/run/secrets/notify_url");
        }
    }

    /// <summary>Reads the OPDS password from the file <c>password_file</c> names, if set, like <c>mam_id_file</c>.</summary>
    private static void ResolveOpdsPasswordFile(FetchshelfSettings settings)
    {
        var path = settings.Opds?.PasswordFile;
        if (settings.Opds is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!string.IsNullOrEmpty(settings.Opds.Password))
        {
            throw new ConfigurationException("Invalid configuration: set either 'password' or 'password_file' in [opds], not both.");
        }

        settings.Opds.Password = ReadSecretFile("password_file", path, "/run/secrets/opds_password");
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
        for (var i = 0; i < settings.QBittorrent.Count; i++)
        {
            var connection = settings.QBittorrent[i];
            var label = ConfigValueValidator.Label("qbittorrent", connection.Name, i);
            if (string.IsNullOrWhiteSpace(connection.Url))
            {
                throw new ConfigurationException($"Invalid configuration: {label} is missing 'url'.");
            }

            if (string.IsNullOrWhiteSpace(connection.Username))
            {
                throw new ConfigurationException($"Invalid configuration: {label} is missing 'username'.");
            }

            if (string.IsNullOrWhiteSpace(connection.Password))
            {
                throw new ConfigurationException($"Invalid configuration: {label} is missing 'password'.");
            }
        }
    }

    /// <summary>Gives every library without its own <c>method</c>, <c>metadata_json</c>, <c>metadata_opf</c> or
    /// <c>split_collections</c> the global one. Runs after environment overrides, so
    /// <c>FETCHSHELF_CONF_link_method</c> and the others reach those libraries too.</summary>
    private static void ApplyGlobalLibraryDefaults(FetchshelfSettings settings)
    {
        foreach (var library in settings.Library)
        {
            if (!library.HasOwnMethod)
            {
                library.Method = settings.LinkMethod;
            }

            // A series library's folders hold many books, where one book's metadata file has no place.
            if (!library.HasOwnMetadataJson)
            {
                library.MetadataJson = settings.MetadataJson && !library.IsSeriesLayout;
            }

            if (!library.HasOwnMetadataOpf)
            {
                library.MetadataOpf = settings.MetadataOpf && !library.IsSeriesLayout;
            }

            if (!library.HasOwnSplitCollections)
            {
                library.SplitCollections = settings.SplitCollections;
            }
        }
    }

    private static void ValidateLibraries(FetchshelfSettings settings)
    {
        for (var i = 0; i < settings.Library.Count; i++)
        {
            var library = settings.Library[i];
            var label = ConfigValueValidator.Label("library", library.Name, i);
            if (string.IsNullOrWhiteSpace(library.LibraryDir))
            {
                throw new ConfigurationException($"Invalid configuration: {label} is missing 'library_dir', the folder the books are linked into.");
            }

            var hasCategory = !string.IsNullOrWhiteSpace(library.Category);
            var hasDownloadDir = !string.IsNullOrWhiteSpace(library.DownloadDir);

            if (hasCategory && hasDownloadDir)
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {label} must specify only one of 'category' or 'download_dir', not both.");
            }

            if (!hasCategory && !hasDownloadDir)
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {label} must specify one of 'category' or 'download_dir'.");
            }

            ValidateLayout(library);
            if (library.LanguageInFolder == LanguageInFolder.Other && settings.Languages.Count == 0)
            {
                var which = library.Name is { } name ? $"the [[library]] '{name}'" : $"the [[library]] for '{library.LibraryDir}'";
                throw new ConfigurationException(
                    $"Invalid configuration: {which} has language_in_folder = \"other\", which marks books not in the first of the top-level "
                    + "'languages', but no 'languages' is set.");
            }

            library.Genres = ParseGenreCondition(library, settings.Genres);

            if (library.FolderTemplate is { } template)
            {
                try
                {
                    library.TemplateLayout = FolderLayout.Parse(template);
                }
                catch (FormatException ex)
                {
                    var which = library.Name is { } name ? $"[[library]] '{name}'" : $"the [[library]] for '{library.LibraryDir}'";
                    throw new ConfigurationException($"Invalid configuration: {which} has a folder_template that can't be used: {ex.Message}.", ex);
                }
            }
        }
    }

    /// <summary>
    /// A library's `allow_genres`, `deny_genres` and `fiction`, which need `genres = true`: every name must be one of MAM's
    /// genres, no genre may be in both lists, and `fiction` is "fiction" or "nonfiction".
    /// </summary>
    private static GenreCondition ParseGenreCondition(Library library, bool genresOn)
    {
        var which = library.Name is { } name ? $"the [[library]] '{name}'" : $"the [[library]] for '{library.LibraryDir}'";
        var hasAny = library.AllowGenres.Count > 0 || library.DenyGenres.Count > 0 || library.Fiction is not null;
        if (!hasAny)
        {
            return GenreCondition.None;
        }

        if (!genresOn)
        {
            throw new ConfigurationException(
                $"Invalid configuration: {which} uses 'allow_genres', 'deny_genres' or 'fiction', which need genres = true at the top of the file.");
        }

        HashSet<GenreCategory> Parse(List<string> names, string setting)
        {
            var genres = new HashSet<GenreCategory>();
            foreach (var genreName in names)
            {
                if (!GenreLabels.TryParse(genreName, out var genre))
                {
                    throw new ConfigurationException(
                        $"Invalid configuration: {which} has '{genreName}' in '{setting}', which is not one of MAM's genres. The genres are: "
                        + string.Join(", ", GenreLabels.All.Select(GenreLabels.Label)) + ".");
                }

                genres.Add(genre);
            }

            return genres;
        }

        var allow = Parse(library.AllowGenres, "allow_genres");
        var deny = Parse(library.DenyGenres, "deny_genres");
        if (allow.Intersect(deny).Select(genre => (GenreCategory?)genre).FirstOrDefault() is { } both)
        {
            throw new ConfigurationException(
                $"Invalid configuration: {which} has '{GenreLabels.Label(both)}' in both 'allow_genres' and 'deny_genres'.");
        }

        FictionCategory? fiction = null;
        if (library.Fiction is { } fictionName)
        {
            if (!GenreLabels.TryParseFiction(fictionName, out var parsed))
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {which} has fiction = \"{fictionName}\"; it takes \"fiction\" or \"nonfiction\".");
            }

            fiction = parsed;
        }

        return new GenreCondition(allow, deny, fiction);
    }

    /// <summary>
    /// The series layout's settings: only on a series library, and a series library has no per-book files or folder
    /// template, as its folders hold many books.
    /// </summary>
    private static void ValidateLayout(Library library)
    {
        var which = library.Name is { } name ? $"the [[library]] '{name}'" : $"the [[library]] for '{library.LibraryDir}'";
        if (!library.IsSeriesLayout)
        {
            if (library.HasSeriesSettings)
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {which} sets 'oneshots_dir' or 'series_json', which only a library with layout = \"series\" uses.");
            }

            return;
        }

        if (library.FolderTemplate is not null)
        {
            throw new ConfigurationException(
                $"Invalid configuration: {which} has layout = \"series\" and a 'folder_template'; the series layout names its folders and files itself.");
        }

        if ((library.HasOwnMetadataJson && library.MetadataJson) || (library.HasOwnMetadataOpf && library.MetadataOpf))
        {
            throw new ConfigurationException(
                $"Invalid configuration: {which} has layout = \"series\" and 'metadata_json' or 'metadata_opf'; a series folder holds many books, "
                + "so it can't have one book's metadata file. Its series.json ('series_json') describes the series.");
        }

        var oneshots = library.OneshotsDir;
        if (string.IsNullOrWhiteSpace(oneshots) || oneshots.Contains('/') || oneshots.Contains('\\') || oneshots is "." or ".."
            || FileNameCleaner.Clean(oneshots, library.FolderNames) != oneshots)
        {
            throw new ConfigurationException(
                $"Invalid configuration: {which} has an 'oneshots_dir' that is not a folder name: '{oneshots}'. It is one folder right under library_dir, such as \"_oneshots\".");
        }
    }

    private static void ValidateAutograbs(FetchshelfSettings settings)
    {
        for (var i = 0; i < settings.Autograb.Count; i++)
        {
            var autograb = settings.Autograb[i];
            var label = ConfigValueValidator.Label("autograb", autograb.Name, i);
            if (autograb.Type is null)
            {
                throw new ConfigurationException($"Invalid configuration: {label} is missing 'type'.");
            }

            if (autograb.MaxActiveDownloads is not null && string.IsNullOrWhiteSpace(autograb.Name))
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {label} sets 'max_active_downloads' and must also set 'name'.");
            }

            // Its queued torrents are matched to it by name when they are downloaded.
            if ((autograb.UnsatBuffer is not null || autograb.WedgeBuffer is not null) && string.IsNullOrWhiteSpace(autograb.Name))
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {label} sets 'unsat_buffer' or 'wedge_buffer' and must also set 'name'.");
            }

            if (!AutograbSearchOptions.TryParseSortBy(autograb.SortBy, out _))
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {label} has an unrecognized 'sort_by' value '{autograb.SortBy}'; " +
                    $"expected one of {AutograbSearchOptions.SortByNames}.");
            }

            var unknownField = autograb.SearchIn.FirstOrDefault(f => !AutograbSearchOptions.TryParseSearchField(f, out _));
            if (unknownField is not null)
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {label} has an unrecognized 'search_in' field '{unknownField}'; " +
                    $"expected one of {AutograbSearchOptions.SearchInNames}.");
            }
        }
    }

    /// <summary>
    /// Checks the top-level <c>languages</c> and gives it to every <c>[[autograb]]</c>, <c>[[hardcover_list.grab]]</c>,
    /// <c>[[file_list.grab]]</c>, <c>[[bookmarks_list.grab]]</c> and <c>[[follow.grab]]</c> block that doesn't set its own. <c>[[tag]]</c> rules are left alone on purpose: they sort out what was selected,
    /// so a default language there would stop a rule matching a book a block explicitly allowed in another language.
    /// Runs after environment overrides, so
    /// <c>FETCHSHELF_CONF_languages</c> reaches the blocks too.
    /// </summary>
    private static void ApplyGlobalLanguages(FetchshelfSettings settings)
    {
        var unknown = settings.Languages.FirstOrDefault(name => !Core.Metadata.LanguageNameResolver.TryResolve(name, out _));
        if (unknown is not null)
        {
            throw new ConfigurationException($"Invalid configuration: unrecognized language '{unknown}' in the top-level 'languages'.");
        }

        var blocks = settings.Autograb.Cast<SharedFilterConfig>()
            .Concat(settings.HardcoverList.SelectMany(list => list.Grab))
            .Concat(settings.FileList.SelectMany(list => list.Grab))
            .Concat(settings.BookmarksList.SelectMany(list => list.Grab))
            .Concat(settings.Follow?.Grab ?? []);
        foreach (var block in blocks.Where(block => block.Languages is null))
        {
            block.Languages = [.. settings.Languages];
        }
    }

    private static void ValidateOpds(FetchshelfSettings settings)
    {
        if (settings.Opds is not { } opds)
        {
            return;
        }

        if (string.IsNullOrEmpty(opds.Username) != string.IsNullOrEmpty(opds.Password))
        {
            throw new ConfigurationException(
                "Invalid configuration: [opds] needs both 'username' and 'password' (or 'password_file') for a login, or neither.");
        }

        if (opds.Username is { } username && username.Contains(':', StringComparison.Ordinal))
        {
            throw new ConfigurationException("Invalid configuration: [opds] 'username' can't contain ':', which HTTP Basic login uses as the separator.");
        }

        var names = settings.Library.Select(library => library.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var unknown = opds.Libraries.FirstOrDefault(name => !names.Contains(name));
        if (unknown is not null)
        {
            throw new ConfigurationException(
                $"Invalid configuration: [opds] 'libraries' names '{unknown}', but no [[library]] has that name.");
        }
    }

    /// <summary>
    /// Each <c>allowed_hosts</c> entry is a host name or IP address, optionally starting with <c>*.</c> for its subdomains:
    /// no scheme, port or path, which host filtering would never match.
    /// </summary>
    private static void ValidateAllowedHosts(FetchshelfSettings settings)
    {
        foreach (var entry in settings.AllowedHosts)
        {
            if (!AllowedHostPattern().IsMatch(entry))
            {
                var bare = AllowedHostBare().Match(entry) is { Success: true } match ? match.Groups["host"].Value : "fetchshelf.lan";
                throw new ConfigurationException(
                    $"Invalid configuration: allowed_hosts: write '{bare}', not '{entry}': a host name or IP address only, without a scheme, port or path.");
            }
        }
    }

    /// <summary>A host name (with an optional <c>*.</c> in front), an IPv4 address, or an IPv6 address in brackets.</summary>
    [GeneratedRegex(@"^((\*\.)?[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*|\[[0-9A-Fa-f:.]+\])$")]
    private static partial Regex AllowedHostPattern();

    /// <summary>The host part of an entry written as an address, for the error's suggestion.</summary>
    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9+.-]*://)?(?<host>\[[^\]]+\]|[^:/?#\s]+)")]
    private static partial Regex AllowedHostBare();

    private static void ValidateAudiobookshelf(FetchshelfSettings settings)
    {
        if (settings.Audiobookshelf is not { } abs)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(abs.Url))
        {
            throw new ConfigurationException("Invalid configuration: the [audiobookshelf] section is missing 'url', the address of Audiobookshelf.");
        }

        if (string.IsNullOrWhiteSpace(abs.Token))
        {
            throw new ConfigurationException("Invalid configuration: the [audiobookshelf] section is missing 'token', an Audiobookshelf API token.");
        }

        if (abs.PathMapping.Count == 0)
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

    /// <summary>
    /// Checks the `[upgrade]` section and gives it its default grab block: without one, upgrades are taken only when
    /// free. Its blocks don't get the top-level `languages`: a better copy is searched in the language of the book.
    /// </summary>
    private static void ValidateUpgrade(FetchshelfSettings settings)
    {
        if (settings.Upgrade is not { } upgrade)
        {
            return;
        }

        var libraries = settings.Library.Select(library => library.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (upgrade.Libraries.FirstOrDefault(name => !libraries.Contains(name)) is { } unknown)
        {
            throw new ConfigurationException(
                $"Invalid configuration: [upgrade] 'libraries' names '{unknown}', but no [[library]] has that name.");
        }

        if (upgrade.Grab.Any(g => g.Cost is Core.AutoDownloader.ConfiguredCost.MetadataOnly or Core.AutoDownloader.ConfiguredCost.MetadataOnlyAdd))
        {
            throw new ConfigurationException(
                "Invalid configuration: the [upgrade] section has a 'grab' block with a metadata-only 'cost'; the Upgrade job downloads books.");
        }

        if (upgrade.Grab.Count == 0)
        {
            upgrade.Grab.Add(new GrabEntry { Cost = Core.AutoDownloader.ConfiguredCost.Free });
        }
    }

    private static void ValidateFollow(FetchshelfSettings settings)
    {
        if (settings.Follow is not { } follow)
        {
            return;
        }

        if (follow.Grab.Count == 0)
        {
            throw new ConfigurationException(
                "Invalid configuration: the [follow] section must have at least one [[follow.grab]] block, which says what the Follow job may take and pay.");
        }

        if (follow.PreferFormat is not (null or Core.Metadata.FilterMainType.Audio or Core.Metadata.FilterMainType.Ebook))
        {
            throw new ConfigurationException("Invalid configuration: the [follow] section has a 'prefer_format' that is not 'audio' or 'ebook'.");
        }

        if (follow.Grab.Any(g => g.Cost is Core.AutoDownloader.ConfiguredCost.MetadataOnly or Core.AutoDownloader.ConfiguredCost.MetadataOnlyAdd))
        {
            throw new ConfigurationException(
                "Invalid configuration: the [follow] section has a 'grab' block with a metadata-only 'cost'; the Follow job downloads books.");
        }

        if (follow.AudiobookshelfSeries != AudiobookshelfSeriesMode.Off && settings.Audiobookshelf is null)
        {
            throw new ConfigurationException(
                "Invalid configuration: the [follow] section's 'audiobookshelf_series' needs an [audiobookshelf] section to read the series from.");
        }

        if (follow.AudiobookshelfSeries == AudiobookshelfSeriesMode.Off && follow.AudiobookshelfLibraries.Count > 0)
        {
            throw new ConfigurationException(
                "Invalid configuration: the [follow] section sets 'audiobookshelf_libraries' but not 'audiobookshelf_series', so no library would be read.");
        }
    }

    private static void ValidateHardcover(FetchshelfSettings settings)
    {
        if (settings.Hardcover is { } hardcover && string.IsNullOrWhiteSpace(hardcover.Token))
        {
            throw new ConfigurationException("Invalid configuration: the [hardcover] section needs 'token' or 'token_file'.");
        }

        // Each status or list is stored as one list, so two blocks on the same one would overwrite each other's state.
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < settings.HardcoverList.Count; i++)
        {
            var list = settings.HardcoverList[i];
            var label = ConfigValueValidator.Label("hardcover_list", list.Name, i);
            if (string.IsNullOrWhiteSpace(list.List))
            {
                list.List = null;
            }

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

            var source = list.List is { } slug ? $"list = \"{slug}\"" : $"status = \"{UnderscoredNamingConvention.Instance.Apply(list.Status!.Value.ToString())}\"";
            if (!seen.TryAdd(source, label))
            {
                throw new ConfigurationException(
                    $"Invalid configuration: {label} and {seen[source]} both read {source}; one block per status or list.");
            }

            ValidateListSearch(label, list.Grab, list.PreferFormat);
        }
    }

    /// <summary>
    /// Checks every `[[file_list]]` and resolves its `path` against the config file's folder. Whether the file is there
    /// is not checked: it may be put there later, and the list's job fails until it is.
    /// </summary>
    private static void ValidateFileLists(FetchshelfSettings settings, string configFilePath)
    {
        var configFolder = Path.GetDirectoryName(Path.GetFullPath(configFilePath)) ?? Path.GetFullPath(".");
        foreach (var list in settings.FileList)
        {
            var label = string.IsNullOrWhiteSpace(list.Name) ? "a [[file_list]] section" : $"the [[file_list]] '{list.Name}'";
            if (string.IsNullOrWhiteSpace(list.Path))
            {
                throw new ConfigurationException($"Invalid configuration: {label} needs a 'path' to the list file.");
            }

            ValidateListSearch(label, list.Grab, list.PreferFormat);
            list.FullPath = Path.GetFullPath(Path.Combine(configFolder, list.Path));
        }
    }

    /// <summary>
    /// Checks the `[[bookmarks_list]]` (that there is at most one is checked with the ranges): no `prefer_format`, and no bookmarks autograb beside it, as both would
    /// select the same torrents.
    /// </summary>
    private static void ValidateBookmarksList(FetchshelfSettings settings)
    {
        if (settings.BookmarksList is not [var list])
        {
            return;
        }

        var label = string.IsNullOrWhiteSpace(list.Name) ? "the [[bookmarks_list]] section" : $"the [[bookmarks_list]] '{list.Name}'";
        if (list.PreferFormat is not null)
        {
            throw new ConfigurationException(
                $"Invalid configuration: {label} can't have 'prefer_format': a bookmark is one torrent, of one format already.");
        }

        if (settings.Autograb.FirstOrDefault(a => a.Type?.Kind == AutograbType.Bookmarks.Kind) is { } autograb)
        {
            var autograbLabel = string.IsNullOrWhiteSpace(autograb.Name) ? "an [[autograb]] with type = \"bookmarks\"" : $"the [[autograb]] '{autograb.Name}'";
            throw new ConfigurationException(
                $"Invalid configuration: {label} and {autograbLabel} would both select the bookmarked torrents; keep one of them. "
                + "The list shows each bookmark on the Lists page; the autograb has the shared filter's full options.");
        }

        ValidateListSearch(label, list.Grab, preferFormat: null);
    }

    /// <summary>What every list needs to search MAM: a `grab` block, none of them metadata-only, and a valid `prefer_format`.</summary>
    private static void ValidateListSearch(string label, List<GrabEntry> grab, Core.Metadata.FilterMainType? preferFormat)
    {
        if (grab.Count == 0)
        {
            throw new ConfigurationException($"Invalid configuration: {label} must have at least one 'grab' block.");
        }

        if (preferFormat is not (null or Core.Metadata.FilterMainType.Audio or Core.Metadata.FilterMainType.Ebook))
        {
            throw new ConfigurationException($"Invalid configuration: {label} has a 'prefer_format' that is not 'audio' or 'ebook'.");
        }

        if (grab.Any(g => g.Cost is Core.AutoDownloader.ConfiguredCost.MetadataOnly or Core.AutoDownloader.ConfiguredCost.MetadataOnlyAdd))
        {
            throw new ConfigurationException(
                $"Invalid configuration: {label} has a 'grab' block with a metadata-only 'cost'; lists download books.");
        }
    }
}
