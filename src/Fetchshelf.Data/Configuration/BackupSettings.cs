namespace Fetchshelf.Data.Configuration;

/// <summary>
/// The optional `[backup]` section: the Backup job, which copies the database while Fetchshelf runs, and the copy taken
/// before an upgrade changes the database. Absent (null on <see cref="FetchshelfSettings.Backup"/>) means no backups.
/// </summary>
public sealed class BackupSettings
{
    /// <summary>Where backups go. A relative path is relative to the data file's folder (see <see cref="ResolveFolder"/>).</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>Minutes between scheduled backups; 0 means only before upgrades and from the Jobs page.</summary>
    public int Interval { get; set; } = 1440;

    /// <summary>How many scheduled backups are kept; the oldest beyond that are deleted after each one.</summary>
    public int Keep { get; set; } = 7;

    /// <summary>Whether a backup is taken at startup before migrations or data upgrades change the database.</summary>
    public bool BeforeUpgrade { get; set; } = true;

    public const int MaxKeep = 365;

    /// <summary>How many before-upgrade backups are kept, apart from <see cref="Keep"/>.</summary>
    public const int KeepBeforeUpgrade = 3;

    /// <summary>
    /// The full path of the backup folder: <see cref="Folder"/> as it is when absolute, else under the folder of
    /// <paramref name="dataFilePath"/>. The data file's own folder is refused, so a backup is never mistaken for
    /// `data.db` and its `-wal`/`-shm` files when that folder is copied or restored.
    /// </summary>
    /// <exception cref="ConfigurationException">The folder is the data file's own folder.</exception>
    public string ResolveFolder(string dataFilePath)
    {
        var dataFolder = Path.GetDirectoryName(Path.GetFullPath(dataFilePath)) ?? Path.GetFullPath(".");
        var folder = Path.GetFullPath(Path.Combine(dataFolder, Folder));
        if (string.Equals(Path.TrimEndingDirectorySeparator(folder), Path.TrimEndingDirectorySeparator(dataFolder), StringComparison.Ordinal))
        {
            throw new ConfigurationException(
                $"Invalid configuration: [backup] 'folder' is the database's own folder ({dataFolder}); "
                + "use a subfolder such as \"backups\", or better a folder on another disk.");
        }

        return folder;
    }
}
