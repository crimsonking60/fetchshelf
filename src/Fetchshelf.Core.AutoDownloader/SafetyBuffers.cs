namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// A block's own `unsat_buffer` and `wedge_buffer`, either of which may be unset, for the selections it makes: those
/// whose grabber name is <paramref name="GrabberName"/>, or, with <paramref name="IsPrefix"/>, starts with it, as the
/// Follow job's "Follow: &lt;series&gt;" names do.
/// </summary>
public sealed record BlockBuffers(string GrabberName, int? UnsatBuffer, int? WedgeBuffer, bool IsPrefix = false);

/// <summary>
/// The `unsat_buffer` and `wedge_buffer` a queued torrent is held to. They are looked up from the current settings
/// when the torrent is downloaded, not stored when it is queued, so a changed setting also applies to torrents already
/// waiting. A torrent follows the buffers of the block that queued it, found by its grabber name (an exact name before a
/// prefix); anything else, including a manual download and a torrent whose block has been removed or renamed, follows
/// the global ones.
/// </summary>
public sealed class SafetyBuffers(int globalUnsatBuffer, int globalWedgeBuffer, IReadOnlyList<BlockBuffers> blocks)
{
    public int UnsatBufferFor(string? grabberName) =>
        BlockFor(grabberName)?.UnsatBuffer ?? globalUnsatBuffer;

    public int WedgeBufferFor(string? grabberName) =>
        BlockFor(grabberName)?.WedgeBuffer ?? globalWedgeBuffer;

    /// <summary>The block that makes selections under this name; names are unique, which the configuration loader makes sure of.</summary>
    private BlockBuffers? BlockFor(string? grabberName) =>
        grabberName is null
            ? null
            : blocks.FirstOrDefault(b => !b.IsPrefix && b.GrabberName == grabberName)
                ?? blocks.FirstOrDefault(b => b.IsPrefix && grabberName.StartsWith(b.GrabberName, StringComparison.Ordinal));
}
