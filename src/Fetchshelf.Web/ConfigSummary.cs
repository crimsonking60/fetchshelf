using System.Globalization;
using System.Text;
using Fetchshelf.Data.Configuration;
using YamlDotNet.Serialization.NamingConventions;

namespace Fetchshelf.Web;

/// <summary>
/// What a loaded configuration amounts to, as plain text for <c>--check-config</c>: the jobs under the names and
/// schedules the Jobs page will show, the libraries, and the safety settings. Secrets are only reported as set or
/// not, never shown.
/// </summary>
public static class ConfigSummary
{
    public static string Build(FetchshelfSettings settings, string configFilePath, bool mamIdFromEnvironment)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Configuration OK: {configFilePath}");
        text.AppendLine();

        var mamIdSource = settings.MamIdFile is not null ? " (from mam_id_file)" : mamIdFromEnvironment ? " (from FETCHSHELF_CONF_mam_id)" : "";
        text.AppendLine(settings.BackgroundJobsEnabled
            ? $"MAM: mam_id set{mamIdSource}; background jobs run"
            : "MAM: no mam_id; the web UI runs, but no background job does");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Safety: min_ratio {settings.MinRatio.ToString(CultureInfo.InvariantCulture)}, unsat_buffer {settings.UnsatBuffer}, wedge_buffer {settings.WedgeBuffer}");

        AppendQBittorrent(text, settings);
        AppendLibraries(text, settings);
        AppendJobs(text, settings);

        text.AppendLine(CultureInfo.InvariantCulture, $"Tag rules: {settings.Tag.Count}");
        if (settings.Audiobookshelf is { } abs)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Audiobookshelf: {abs.Url}{Count(abs.PathMapping.Count, "path mapping")}");
        }

        if (settings.Hardcover is not null)
        {
            text.AppendLine("Hardcover: token set");
        }

        if (settings.Opds is { } opds)
        {
            text.AppendLine(opds.Libraries.Count == 0 ? "OPDS: the ebooks of every library" : $"OPDS: {string.Join(", ", opds.Libraries)}");
        }

        AppendNotifications(text, settings);

        return text.ToString();
    }

    /// <summary>The `[[notify]]` blocks: name, format and events. The URLs are secrets, so they are only reported as set.</summary>
    private static void AppendNotifications(StringBuilder text, FetchshelfSettings settings)
    {
        if (settings.Notify.Count == 0)
        {
            return;
        }

        text.AppendLine(string.IsNullOrWhiteSpace(settings.WebUrl)
            ? "Notifications: no web_url, so messages carry no links"
            : $"Notifications: messages link to {settings.WebUrl}");
        var rows = NotifyTarget.From(settings).Select(target => (target.Name,
            $"{Fetchshelf.Core.Notifications.NotificationFormatNames.Name(target.Format)}, url set, "
            + (target.Events.Count == Fetchshelf.Core.Notifications.NotificationEvents.All.Count
                ? "all events"
                : string.Join(", ", Fetchshelf.Core.Notifications.NotificationEvents.All.Where(target.Events.Contains)
                    .Select(Fetchshelf.Core.Notifications.NotificationEvents.Name))))).ToList();
        AppendRows(text, rows);
    }

    private static void AppendQBittorrent(StringBuilder text, FetchshelfSettings settings)
    {
        if (settings.QBittorrent.Count == 0)
        {
            text.AppendLine("qBittorrent: none configured");
            return;
        }

        text.AppendLine("qBittorrent:");
        foreach (var connection in settings.QBittorrent)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {connection.Url}{Count(connection.PathMapping.Count, "path mapping")}");
        }
    }

    private static void AppendLibraries(StringBuilder text, FetchshelfSettings settings)
    {
        if (settings.Library.Count == 0)
        {
            text.AppendLine("Libraries: none, so the Link job links nothing");
            return;
        }

        text.AppendLine("Libraries:");
        var libraries = settings.Library.Select((library, index) =>
        {
            var match = !string.IsNullOrWhiteSpace(library.Category) ? $"category {library.Category}" : $"download_dir {library.DownloadDir}";
            var metadataFiles = new[] { library.MetadataJson ? "metadata.json" : null, library.MetadataOpf ? "metadata.opf" : null }.OfType<string>();
            var details = string.Join(", ", new[] { Underscored(library.Method.ToString()) }.Concat(metadataFiles));
            return (Name: library.Name ?? $"library {index + 1}", Match: match, Target: $"-> {library.LibraryDir}  {details}");
        }).ToList();
        var matchWidth = libraries.Max(library => library.Match.Length);
        var rows = libraries.Select(library => (library.Name, $"{library.Match.PadRight(matchWidth)}  {library.Target}")).ToList();
        AppendRows(text, rows);
    }

    private static void AppendJobs(StringBuilder text, FetchshelfSettings settings)
    {
        if (!settings.BackgroundJobsEnabled)
        {
            text.AppendLine("Jobs: none without mam_id");
            return;
        }

        // In the order Program.cs registers them, which is the order of the Jobs page.
        var rows = new List<(string Name, string Details)>
        {
            (JobNames.Link, Schedule(settings.LinkInterval)),
            (JobNames.Clean, "after every Link run"),
        };
        if (settings.Audiobookshelf is { } abs)
        {
            rows.Add((JobNames.Audiobookshelf, Schedule(abs.Interval)));
        }

        for (var i = 0; i < settings.Autograb.Count; i++)
        {
            var entry = settings.Autograb[i];
            var type = entry.Type?.UploaderId is { } uploader ? $"uploader {uploader}" : entry.Type?.Kind;
            rows.Add((AutoDownloaderJobNames.Autograb(entry, i),
                $"autograb {type}, cost {Underscored(entry.Cost.ToString())}, {Schedule(entry.SearchInterval ?? settings.SearchInterval)}{DryRun(entry.DryRun)}"));
        }

        for (var i = 0; i < settings.Snatchlist.Count; i++)
        {
            var entry = settings.Snatchlist[i];
            rows.Add((AutoDownloaderJobNames.Snatchlist(entry, i),
                $"snatchlist {Underscored(entry.Type.ToString())}, cost {Underscored(entry.Cost.ToString())}, " +
                $"{Schedule(entry.SearchInterval ?? settings.SearchInterval)}{DryRun(entry.DryRun)}"));
        }

        for (var i = 0; i < settings.HardcoverList.Count; i++)
        {
            var entry = settings.HardcoverList[i];
            var source = entry.Status is { } status ? $"Hardcover {Underscored(status.ToString())}" : $"Hardcover list '{entry.List}'";
            rows.Add((AutoDownloaderJobNames.HardcoverList(entry, i),
                $"{source}, {entry.Grab.Count} grab block{(entry.Grab.Count == 1 ? "" : "s")}, " +
                $"{Schedule(entry.SearchInterval ?? settings.SearchInterval)}{DryRun(entry.DryRun)}"));
        }

        var afterSelections = settings.DownloadAfterSelection ? ", and after selections" : "";
        rows.Add((JobNames.Download, settings.SearchInterval > 0
            ? $"every {settings.SearchInterval} min{afterSelections}"
            : $"only from the Jobs page{afterSelections}"));

        text.AppendLine("Jobs:");
        AppendRows(text, rows);
    }

    private static void AppendRows(StringBuilder text, IReadOnlyList<(string Name, string Details)> rows)
    {
        var width = rows.Max(row => row.Name.Length);
        foreach (var (name, details) in rows)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {name.PadRight(width)}  {details}");
        }
    }

    private static string Schedule(int minutes) => minutes > 0 ? $"every {minutes} min" : "only from the Jobs page";

    private static string DryRun(bool dryRun) => dryRun ? ", dry run" : "";

    private static string Count(int count, string noun) => count == 0 ? "" : $" ({count} {noun}{(count == 1 ? "" : "s")})";

    private static string Underscored(string name) => UnderscoredNamingConvention.Instance.Apply(name);
}
