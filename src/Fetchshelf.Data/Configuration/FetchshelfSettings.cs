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

    /// <summary>`dry_job_buttons`: show <b>Dry run</b> next to <b>Run now</b> on the Jobs page for the jobs that have one.</summary>
    public bool DryJobButtons { get; set; } = true;

    /// <summary>
    /// Undocumented, for maintainers: when true, every page starts with a thin red bar, to tell a development install
    /// from a production one at a glance.
    /// </summary>
    public bool DevelopmentBanner { get; set; }

    public bool ExcludeNarratorInLibraryDir { get; set; }

    /// <summary>The `metadata_json` of every `[[library]]` that does not set its own.</summary>
    public bool MetadataJson { get; set; } = true;

    /// <summary>The `metadata_opf` of every `[[library]]` that does not set its own.</summary>
    public bool MetadataOpf { get; set; }

    /// <summary>
    /// `covers_from_mam`: how many covers each Link run fetches from MAM for book folders that have no image; 0 fetches
    /// none. At most <see cref="MaxCoversFromMam"/>.
    /// </summary>
    public int CoversFromMam { get; set; }

    /// <summary>The highest `covers_from_mam`: the covers come from a part of MAM that isn't authorized for automation, so few per run.</summary>
    public const int MaxCoversFromMam = 100;

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

    /// <summary>
    /// qBittorrent categories the web UI offers alongside the `[[library]]` ones, for torrents that belong in no
    /// library. Offering one links nothing: only a library's category or save path does that.
    /// </summary>
    public List<string> ExtraCategories { get; set; } = [];

    /// <summary>
    /// The `languages` of every `[[autograb]]` and `[[hardcover_list.grab]]` block that doesn't set its own, and the
    /// Search page's default. Empty means any language. `[[tag]]` rules don't use it.
    /// </summary>
    public List<string> Languages { get; set; } = [];

    public List<string> AudioTypes { get; set; } = ["m4b", "m4a", "mp4", "mp3", "ogg"];

    public List<string> EbookTypes { get; set; } = ["epub", "cbz", "pdf", "mobi", "azw3", "azw", "cbr"];

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

    /// <summary>Optional `[seeding]` section; without it the Seeding job doesn't run.</summary>
    public SeedingSettings? Seeding { get; set; }

    /// <summary>Optional `[search]` section.</summary>
    public SearchSettings Search { get; set; } = new();

    /// <summary>Optional `[audiobookshelf]` section. Null when not configured.</summary>
    public AudiobookshelfSettings? Audiobookshelf { get; set; }

    /// <summary>Optional `[hardcover]` section. Null when not configured.</summary>
    public HardcoverSettings? Hardcover { get; set; }

    /// <summary>Optional `[opds]` section. Null when not configured, and then no OPDS catalog is served.</summary>
    public OpdsSettings? Opds { get; set; }

    /// <summary>Repeatable `[[hardcover_list]]` entries.</summary>
    public List<HardcoverListEntry> HardcoverList { get; set; } = [];

    /// <summary>Repeatable `[[file_list]]` entries.</summary>
    public List<FileListEntry> FileList { get; set; } = [];

    /// <summary>Repeatable in form, `[[bookmarks_list]]`, but at most one.</summary>
    public List<BookmarksListEntry> BookmarksList { get; set; } = [];

    /// <summary>Optional `[follow]` section. Null when not configured, and then series and authors can't be followed.</summary>
    public FollowSettings? Follow { get; set; }

    /// <summary>Optional `[upgrade]` section. Null when not configured, and then no Upgrade job looks for better copies.</summary>
    public UpgradeSettings? Upgrade { get; set; }

    /// <summary>Optional `[bonus]` section. Null when not configured, and then no bonus points are ever spent.</summary>
    public BonusSettings? Bonus { get; set; }

    /// <summary>Optional `[backup]` section. Null when not configured, and then Fetchshelf takes no backups.</summary>
    public BackupSettings? Backup { get; set; }

    /// <summary>Repeatable `[[notify]]` entries: where to send messages about what Fetchshelf does.</summary>
    public List<NotifyEntry> Notify { get; set; } = [];

    /// <summary>
    /// The address Fetchshelf's web UI answers at, such as <c>https://fetchshelf.example.net</c>, which notifications link to.
    /// Without it, messages carry no links: the address the browser uses isn't known to the server.
    /// </summary>
    public string? WebUrl { get; set; }

    /// <summary>
    /// `allowed_hosts`: the host names Fetchshelf answers to, such as <c>fetchshelf.lan</c>, which stops other web pages
    /// using it through DNS rebinding. Empty answers to any host, as before.
    /// </summary>
    public List<string> AllowedHosts { get; set; } = [];

    /// <summary>False while `mam_id` is empty: then no background job is scheduled or run.</summary>
    public bool BackgroundJobsEnabled => !string.IsNullOrWhiteSpace(MamId);
}
