namespace Fetchshelf.Core.AutoDownloader;

/// <summary>An autograb's own `unsat_buffer` and `wedge_buffer`, either of which may be unset.</summary>
public sealed record AutograbBuffers(string Name, int? UnsatBuffer, int? WedgeBuffer);

/// <summary>
/// The `unsat_buffer` and `wedge_buffer` a queued torrent is held to. They are looked up from the current settings
/// when the torrent is downloaded, not stored when it is queued, so a changed setting also applies to torrents already
/// waiting. A torrent queued by an autograb follows that autograb's own buffers; anything else, including a manual
/// download and a torrent whose autograb has been removed or renamed, follows the global ones.
/// </summary>
public sealed class SafetyBuffers(int globalUnsatBuffer, int globalWedgeBuffer, IReadOnlyList<AutograbBuffers> autograbs)
{
    public int UnsatBufferFor(string? grabberName) =>
        Strictest(grabberName, a => a.UnsatBuffer) ?? globalUnsatBuffer;

    public int WedgeBufferFor(string? grabberName) =>
        Strictest(grabberName, a => a.WedgeBuffer) ?? globalWedgeBuffer;

    /// <summary>Should several autograbs share a name, the largest of their buffers applies.</summary>
    private int? Strictest(string? grabberName, Func<AutograbBuffers, int?> buffer) =>
        grabberName is null
            ? null
            : autograbs.Where(a => a.Name == grabberName).Select(buffer).Max();
}
