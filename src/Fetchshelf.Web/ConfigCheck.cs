using Fetchshelf.Core.Lists;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web;

/// <summary>
/// <c>dotnet Fetchshelf.Web.dll --check-config</c>: loads the configuration exactly as startup would, environment
/// overrides included, and prints what it understood (0) or why it would not start (1). It contacts no service,
/// leaves the database alone, and doesn't create a missing file, so it is safe to run next to the running instance.
/// </summary>
public static class ConfigCheck
{
    public const string Argument = "--check-config";

    public static int Run(string configFilePath, TextWriter output)
    {
        if (!File.Exists(configFilePath))
        {
            output.WriteLine($"No configuration file at {configFilePath}. On its first start, Fetchshelf creates one with an empty mam_id.");
            return 1;
        }

        try
        {
            var settings = ConfigLoader.Load(configFilePath);
            // Checked against the data file startup would use, as a relative backup folder is under its folder.
            settings.Backup?.ResolveFolder(Environment.GetEnvironmentVariable("FETCHSHELF_DATA_FILE") ?? "data.db");
            var mamIdFromEnvironment = Environment.GetEnvironmentVariable("FETCHSHELF_CONF_mam_id") is not null;
            output.Write(ConfigSummary.Build(settings, configFilePath, mamIdFromEnvironment));
            WriteListFiles(settings, output);
            return 0;
        }
        catch (ConfigurationException ex)
        {
            output.WriteLine($"Fetchshelf would not start with {configFilePath}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// What each `[[file_list]]` file holds. A missing file doesn't stop Fetchshelf starting, as it may be put there
    /// later; until it is, the list's job fails.
    /// </summary>
    private static void WriteListFiles(FetchshelfSettings settings, TextWriter output)
    {
        if (settings.FileList.Count == 0)
        {
            return;
        }

        output.WriteLine("List files:");
        for (var i = 0; i < settings.FileList.Count; i++)
        {
            var entry = settings.FileList[i];
            string text;
            try
            {
                text = FileListSource.ReadTextAsync(entry).GetAwaiter().GetResult();
            }
            catch (ListFileException ex)
            {
                output.WriteLine($"  {AutoDownloaderJobNames.FileList(entry, i)}: {ex.Message} ({entry.FullPath}); the job fails until it can be read");
                continue;
            }

            var contents = ListFileReader.Read(text, entry.Shelf);
            var books = contents.Books.Count == 1 ? "1 book" : $"{contents.Books.Count} books";
            var format = contents.Format == ListFileFormat.Csv ? "CSV" : "text";
            var problems = contents.Problems.Count == 0 ? ""
                : $"; lines not read: {string.Join(", ", contents.Problems.Select(p => $"{p.Line} ({p.Reason})"))}";
            var shelf = entry.Shelf is not null && !contents.HasShelfColumn ? "; no shelf column, so 'shelf' is ignored" : "";
            output.WriteLine($"  {AutoDownloaderJobNames.FileList(entry, i)}: {entry.Path}: {books} ({format}){problems}{shelf}");
        }
    }
}
