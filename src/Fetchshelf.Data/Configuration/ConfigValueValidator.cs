using System.Globalization;
using System.Text.RegularExpressions;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Notifications;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// Checks the values <see cref="ConfigLoader"/> has read: numbers within the range that makes sense, and every
/// shared torrent filter convertible, so a mistake stops startup instead of failing a job later. Runs after
/// environment overrides, so overridden values are checked too.
/// </summary>
internal static partial class ConfigValueValidator
{
    /// <summary>The longest interval: 30 days, well inside what the scheduler's delay accepts (about 49 days).</summary>
    public const int MaxIntervalMinutes = 43_200;

    public static void ValidateRanges(FetchshelfSettings settings)
    {
        if (!double.IsFinite(settings.MinRatio) || settings.MinRatio <= 0)
        {
            throw Invalid($"'min_ratio' must be a number above 0; got {Format(settings.MinRatio)}.");
        }

        if (settings.WebPort is < 1 or > 65_535)
        {
            throw Invalid($"'web_port' must be between 1 and 65535; got {settings.WebPort}.");
        }

        CheckInterval("search_interval", settings.SearchInterval, label: null);
        CheckInterval("link_interval", settings.LinkInterval, label: null);
        CheckAtLeast("unsat_buffer", settings.UnsatBuffer, 0, label: null);
        CheckAtLeast("wedge_buffer", settings.WedgeBuffer, 0, label: null);

        if (settings.Audiobookshelf is { } abs)
        {
            CheckInterval("interval", abs.Interval, "the [audiobookshelf] section");
        }

        var unreadableId = settings.IgnoreTorrents.FirstOrDefault(id =>
            !long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var mamId) || mamId <= 0);
        if (unreadableId is not null)
        {
            throw Invalid($"'ignore_torrents' holds '{unreadableId}', which is not a MAM torrent id; expected whole numbers such as \"123456\".");
        }

        for (var i = 0; i < settings.Autograb.Count; i++)
        {
            var autograb = settings.Autograb[i];
            var label = Label("autograb", autograb.Name, i);
            CheckInterval("search_interval", autograb.SearchInterval, label);
            CheckAtLeast("unsat_buffer", autograb.UnsatBuffer, 0, label);
            CheckAtLeast("wedge_buffer", autograb.WedgeBuffer, 0, label);
            CheckAtLeast("max_active_downloads", autograb.MaxActiveDownloads, 1, label);
            CheckAtLeast("max_pages", autograb.MaxPages, 1, label);

            // mark_removed reads a gap in the results' MAM ids as a sign of a removal; a bookmark list is all gaps.
            if (autograb.MarkRemoved && autograb.Type?.Kind == AutograbType.Bookmarks.Kind)
            {
                throw Invalid($"{label} sets 'mark_removed', which needs a search by id range; bookmark lists aren't one.");
            }
        }

        for (var i = 0; i < settings.QBittorrent.Count; i++)
        {
            var connection = settings.QBittorrent[i];
            var label = Label("qbittorrent", name: null, i);
            CheckDoesSomething("on_cleaned", connection.OnCleaned, label);
            CheckDoesSomething("on_unlinked", connection.OnUnlinked, label);
            CheckDoesSomething("on_invalid_torrent", connection.OnInvalidTorrent, label);
        }

        for (var i = 0; i < settings.Snatchlist.Count; i++)
        {
            var snatchlist = settings.Snatchlist[i];
            var label = Label("snatchlist", snatchlist.Name, i);
            CheckInterval("search_interval", snatchlist.SearchInterval, label);
            CheckAtLeast("max_pages", snatchlist.MaxPages, 1, label);
        }

        for (var i = 0; i < settings.HardcoverList.Count; i++)
        {
            var list = settings.HardcoverList[i];
            CheckInterval("search_interval", list.SearchInterval, Label("hardcover_list", list.Name, i));
        }

        if (settings.Follow is { } follow)
        {
            const string label = "the [follow] section";
            CheckInterval("search_interval", follow.SearchInterval, label);
            CheckAtLeast("searches_per_run", follow.SearchesPerRun, 1, label);
            if (follow.SearchAgainAfter is < 1 or > FollowSettings.MaxSearchAgainAfterHours)
            {
                throw Invalid($"'search_again_after' in {label} must be between 1 and {FollowSettings.MaxSearchAgainAfterHours} hours (30 days); got {follow.SearchAgainAfter}.");
            }
        }

        if (settings.Search.WedgeOver is { } wedgeOver)
        {
            settings.Search.WedgeOverBytes = TryParseSize(wedgeOver)
                ?? throw Invalid($"[search] 'wedge_over' is '{wedgeOver}', which is not a size; {SizeExample}.");
        }
    }

    /// <summary>
    /// Every <c>[[autograb]]</c>, <c>[[snatchlist]]</c> and <c>[[hardcover_list]]</c> becomes a job under its
    /// <c>name</c>, and its selections carry that name. So names must be unique among all three, compared ignoring case,
    /// and must not be a built-in job's, the Search page's grabber name or of the form a block without a name gets.
    /// Library names must be unique too: <c>[opds] libraries</c> and the page filters refer to libraries by name.
    /// </summary>
    public static void ValidateNames(FetchshelfSettings settings)
    {
        var blocks = settings.Autograb.Select((entry, i) => (entry.Name, Label: Label("autograb", entry.Name, i), JobName: AutoDownloaderJobNames.Autograb(entry, i)))
            .Concat(settings.Snatchlist.Select((entry, i) => (entry.Name, Label: Label("snatchlist", entry.Name, i), JobName: AutoDownloaderJobNames.Snatchlist(entry, i))))
            .Concat(settings.HardcoverList.Select((entry, i) => (entry.Name, Label: Label("hardcover_list", entry.Name, i), JobName: AutoDownloaderJobNames.HardcoverList(entry, i))))
            .ToList();

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, label, jobName) in blocks)
        {
            if (name is not null)
            {
                CheckName(name, label);
            }

            if (seen.TryGetValue(jobName, out var earlier))
            {
                throw Invalid($"{earlier} and {label} have the same name; every autograb, snatchlist and Hardcover list needs its own.");
            }

            seen[jobName] = label;
        }

        var libraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < settings.Library.Count; i++)
        {
            if (settings.Library[i].Name is not { } name)
            {
                continue;
            }

            var label = Label("library", name, i);
            if (name.Length == 0 || name.Trim() != name)
            {
                throw Invalid($"{label} has a 'name' that is empty or starts or ends with a space.");
            }

            if (libraries.TryGetValue(name, out var earlier))
            {
                throw Invalid($"{earlier} and {label} have the same name; every library needs its own.");
            }

            libraries[name] = label;
        }
    }

    /// <summary>
    /// Checks every `[[notify]]` block and `web_url`. The URLs are secrets, so no message repeats one: it names the block
    /// and says what is wrong with the setting.
    /// </summary>
    public static void ValidateNotify(FetchshelfSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WebUrl) && !IsWebAddress(settings.WebUrl))
        {
            throw Invalid($"'web_url' must be an http or https address, such as \"https://fetchshelf.example.net\"; got '{settings.WebUrl}'.");
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < settings.Notify.Count; i++)
        {
            var entry = settings.Notify[i];
            var label = Label("notify", entry.Name, i);
            if (entry.Name is { } name && (name.Length == 0 || name.Trim() != name))
            {
                throw Invalid($"{label} has a 'name' that is empty or starts or ends with a space.");
            }

            var effectiveName = NotifyTarget.NameFor(entry, i);
            if (names.TryGetValue(effectiveName, out var earlier))
            {
                throw Invalid($"{earlier} and {label} have the same name; every [[notify]] needs its own.");
            }

            names[effectiveName] = label;

            if (string.IsNullOrWhiteSpace(entry.Url))
            {
                throw Invalid($"{label} needs a 'url' or a 'url_file'.");
            }

            if (!IsWebAddress(entry.Url))
            {
                // Not repeated: the address is a secret.
                throw Invalid($"{label} has a 'url' that is not an http or https address.");
            }

            if (!NotificationFormatNames.TryParse(entry.Format, out _))
            {
                throw Invalid($"{label} has an unknown 'format' '{entry.Format}'; expected one of {NotificationFormatNames.NameList}.");
            }

            if (entry.Events.Count == 0)
            {
                throw Invalid($"{label} has no 'events'; list the events to send, or [\"all\"].");
            }

            var unknown = entry.Events.FirstOrDefault(e => e != NotificationEvents.AllName && !NotificationEvents.TryParse(e, out _));
            if (unknown is not null)
            {
                throw Invalid($"{label} has an unknown event '{unknown}' in 'events'; expected \"all\" or any of {NotificationEvents.NameList}.");
            }
        }
    }

    private static bool IsWebAddress(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host);

    private static void CheckName(string name, string label)
    {
        if (name.Length == 0 || name.Trim() != name)
        {
            throw Invalid($"{label} has a 'name' that is empty or starts or ends with a space.");
        }

        if (JobNames.Reserved.FirstOrDefault(reserved => string.Equals(reserved, name, StringComparison.OrdinalIgnoreCase)) is { } taken)
        {
            throw Invalid($"{label}: '{taken}' is the name of a built-in job; choose another name.");
        }

        if (GeneratedJobNamePattern().IsMatch(name))
        {
            throw Invalid($"{label}: names like '{name}' are given to blocks without a name; choose another name.");
        }
    }

    [GeneratedRegex(@"^(autograb|snatchlist|hardcover_list)\[\d+\]$", RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedJobNamePattern();

    /// <summary>
    /// Converts the filter of every <c>[[autograb]]</c>, <c>[[snatchlist]]</c>, <c>[[hardcover_list]]</c> grab block
    /// and <c>[[tag]]</c> rule as their jobs will, and checks its bounds. Runs after the top-level <c>languages</c> are
    /// handed down, so an inherited language is checked in each block it reaches.
    /// </summary>
    public static void ValidateFilters(FetchshelfSettings settings)
    {
        for (var i = 0; i < settings.Autograb.Count; i++)
        {
            ValidateFilter(settings.Autograb[i], Label("autograb", settings.Autograb[i].Name, i));
        }

        for (var i = 0; i < settings.Snatchlist.Count; i++)
        {
            ValidateFilter(settings.Snatchlist[i], Label("snatchlist", settings.Snatchlist[i].Name, i));
        }

        for (var i = 0; i < settings.HardcoverList.Count; i++)
        {
            var list = settings.HardcoverList[i];
            for (var g = 0; g < list.Grab.Count; g++)
            {
                ValidateFilter(list.Grab[g], $"grab block {g + 1} of {Label("hardcover_list", list.Name, i)}");
            }
        }

        for (var g = 0; g < (settings.Follow?.Grab.Count ?? 0); g++)
        {
            ValidateFilter(settings.Follow!.Grab[g], $"grab block {g + 1} of the [follow] section");
        }

        for (var i = 0; i < settings.Tag.Count; i++)
        {
            ValidateFilter(settings.Tag[i], Label("tag", settings.Tag[i].Name, i));
        }
    }

    private static void ValidateFilter(SharedFilterConfig config, string label)
    {
        TorrentFilter filter;
        try
        {
            filter = SharedFilterConfigMapper.ToTorrentFilter(config);
        }
        catch (ConfigurationException ex)
        {
            throw Invalid($"{label}: {ex.Message.Replace("Invalid configuration: ", "", StringComparison.Ordinal)}");
        }

        CheckBounds("min_seeders", filter.MinSeeders, "max_seeders", filter.MaxSeeders, label);
        CheckBounds("min_leechers", filter.MinLeechers, "max_leechers", filter.MaxLeechers, label);
        CheckBounds("min_snatched", filter.MinSnatched, "max_snatched", filter.MaxSnatched, label);
        CheckAtLeast("max_authors", filter.MaxAuthors, 1, label);

        if (filter.MinSizeBytes > filter.MaxSizeBytes)
        {
            throw Invalid($"{label} has a 'min_size' ({config.MinSize}) above its 'max_size' ({config.MaxSize}).");
        }

        if (filter.UploadedAfter > filter.UploadedBefore)
        {
            throw Invalid($"{label} has an 'uploaded_after' ({config.UploadedAfter}) later than its 'uploaded_before' ({config.UploadedBefore}).");
        }
    }

    public const string SizeExample = "expected a number and a unit, such as \"500 MiB\" or \"1.5 GB\"";

    /// <summary>A size in MAM's syntax as bytes, or null when it can't be read.</summary>
    public static long? TryParseSize(string size)
    {
        try
        {
            return MamSizeParser.ParseBytes(size);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return null;
        }
    }

    private static void CheckInterval(string setting, int? minutes, string? label)
    {
        if (minutes is < 0 or > MaxIntervalMinutes)
        {
            throw Invalid($"{Where(setting, label)} must be between 0 (run only from the Jobs page) and {MaxIntervalMinutes} minutes (30 days); got {minutes}.");
        }
    }

    private static void CheckAtLeast(string setting, long? value, long minimum, string? label)
    {
        if (value < minimum)
        {
            throw Invalid($"{Where(setting, label)} must be {minimum} or more; got {value}.");
        }
    }

    /// <summary>A category-and-tags table with neither is most likely a mistake: it would do nothing.</summary>
    private static void CheckDoesSomething(string setting, QBittorrentCategoryAndTags? value, string label)
    {
        if (value is not null && string.IsNullOrEmpty(value.Category) && value.Tags.Count == 0)
        {
            throw Invalid($"{Where(setting, label)} sets neither a 'category' nor 'tags', so it would do nothing; set one or remove the table.");
        }
    }

    private static void CheckBounds(string minSetting, long? min, string maxSetting, long? max, string label)
    {
        CheckAtLeast(minSetting, min, 0, label);
        CheckAtLeast(maxSetting, max, 0, label);
        if (min > max)
        {
            throw Invalid($"{label} has a '{minSetting}' ({min}) above its '{maxSetting}' ({max}).");
        }
    }

    private static string Where(string setting, string? label) => label is null ? $"'{setting}'" : $"'{setting}' in {label}";

    /// <summary>"the [[autograb]] 'Bookmarks'", or "the 2nd [[autograb]] section" for one without a name.</summary>
    public static string Label(string section, string? name, int index) =>
        string.IsNullOrWhiteSpace(name) ? $"the {Ordinal(index + 1)} [[{section}]] section" : $"the [[{section}]] '{name}'";

    private static string Ordinal(int number) => (number % 100) switch
    {
        11 or 12 or 13 => $"{number}th",
        _ => (number % 10) switch
        {
            1 => $"{number}st",
            2 => $"{number}nd",
            3 => $"{number}rd",
            _ => $"{number}th",
        },
    };

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static ConfigurationException Invalid(string message) => new($"Invalid configuration: {message}");
}
