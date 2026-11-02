namespace Fetchshelf.Clients.Mam;

/// <summary>What a search looks through (bookmarks, new torrents, own uploads, or a specific uploader).</summary>
public sealed record MamSearchTarget
{
    public static readonly MamSearchTarget Bookmarks = new("bookmarks");
    public static readonly MamSearchTarget New = new("torrents");
    public static readonly MamSearchTarget Mine = new("mine");

    // "allRseed" (not "allReseed") is the value MAM actually expects.
    public static readonly MamSearchTarget AllReseed = new("allRseed");
    public static readonly MamSearchTarget MyReseed = new("myReseed");

    public static MamSearchTarget Uploader(long uploaderId) => new($"u{uploaderId}");

    internal string WireValue { get; }

    private MamSearchTarget(string wireValue) => WireValue = wireValue;
}
