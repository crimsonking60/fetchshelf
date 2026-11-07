using YamlDotNet.Serialization;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// Top-level configuration settings. Default values are expressed as property
/// initializers so that a setting absent from the config file (and not overridden by environment
/// variable) takes its documented default.
/// </summary>
public sealed class FetchshelfSettings
{
    /// <summary>
    /// MAM session id (cookie value). Required for background jobs to run. When <see cref="MamIdFile"/> is
    /// set, <c>ConfigLoader</c> fills this from that file.
    /// </summary>
    public string MamId { get; set; } = string.Empty;

    /// <summary>Full path of a file holding the MAM session id; an alternative to setting <see cref="MamId"/>.</summary>
    public string? MamIdFile { get; set; }

    public string WebHost { get; set; } = "0.0.0.0";

    public int WebPort { get; set; } = 3157;

    public double MinRatio { get; set; } = 2;

    public int UnsatBuffer { get; set; } = 10;

    public int WedgeBuffer { get; set; }

    public bool AddTorrentsStopped { get; set; }

    /// <summary>
    /// When true, the Download job starts as soon as an autograb, a Hardcover list or the Search page selects a
    /// torrent. When false, selections wait on the Selected page for the next scheduled Download run.
    /// </summary>
    public bool DownloadAfterSelection { get; set; } = true;

    /// <summary>When true, each web page shows a brief explanation of what it does below its title.</summary>
    public bool PageExplanations { get; set; } = true;

    /// <summary>When true, the Jobs page shows a brief description of what each job does.</summary>
    public bool JobExplanations { get; set; } = true;

    public bool ExcludeNarratorInLibraryDir { get; set; }

    /// <summary>
    /// When true, the database uses SQLite's write-ahead log, so pages can read while a job writes. Off by default:
    /// the log's shared-memory file can't work when the database is on a network share, so it's for local disks only.
    /// </summary>
    public bool DatabaseWal { get; set; }

    /// <summary>The `method` of every `[[library]]` that does not set its own.</summary>
    public LibraryLinkMethod LinkMethod { get; set; } = LibraryLinkMethod.HardlinkOrCopy;

    public int SearchInterval { get; set; } = 30;

    public int LinkInterval { get; set; } = 10;

    public List<string> IgnoreTorrents { get; set; } = [];

    public List<string> AudioTypes { get; set; } = ["m4b", "m4a", "mp4", "mp3", "ogg"];

    public List<string> EbookTypes { get; set; } = ["cbz", "epub", "pdf", "mobi", "azw3", "azw", "cbr"];

    public List<string> MusicTypes { get; set; } = ["flac", "mp3", "pdf"];

    public List<string> RadioTypes { get; set; } = ["mp3"];

    /// <summary>
    /// Repeatable `[[qbittorrent]]` connection entries. Explicit alias since the irregular
    /// "QBittorrent" casing is not safely reconstructible by the underscored naming convention.
    /// </summary>
    [YamlMember(Alias = "qbittorrent")]
    public List<QBittorrentConnection> QBittorrent { get; set; } = [];

    /// <summary>Repeatable `[[library]]` entries.</summary>
    public List<Library> Library { get; set; } = [];

    /// <summary>Repeatable `[[autograb]]` entries.</summary>
    public List<AutograbEntry> Autograb { get; set; } = [];

    /// <summary>Repeatable `[[tag]]` entries.</summary>
    public List<TagRuleEntry> Tag { get; set; } = [];

    /// <summary>Repeatable `[[snatchlist]]` entries.</summary>
    public List<SnatchlistEntry> Snatchlist { get; set; } = [];

    /// <summary>Optional `[search]` section.</summary>
    public SearchSettings Search { get; set; } = new();

    /// <summary>Optional `[audiobookshelf]` section. Null when not configured.</summary>
    public AudiobookshelfSettings? Audiobookshelf { get; set; }

    /// <summary>Optional `[hardcover]` section. Null when not configured.</summary>
    public HardcoverSettings? Hardcover { get; set; }

    /// <summary>Repeatable `[[hardcover_list]]` entries.</summary>
    public List<HardcoverListEntry> HardcoverList { get; set; } = [];

    /// <summary>False while `mam_id` is empty: then no background job is scheduled or run.</summary>
    public bool BackgroundJobsEnabled => !string.IsNullOrWhiteSpace(MamId);
}
