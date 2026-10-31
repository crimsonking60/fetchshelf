namespace Fetchshelf.Core.Metadata;

/// <summary>
/// Tracker content flags. Bit values match the tracker's real flag bitfield
/// (confirmed against the original Rust implementation's `Flags::from_bitfield`/`as_bitfield`):
/// bit 0 is unused, so the flags start at <c>1 &lt;&lt; 1</c>, not <c>1 &lt;&lt; 0</c>.
/// </summary>
[Flags]
public enum BookFlags
{
    None = 0,
    CrudeLanguage = 1 << 1,
    Violence = 1 << 2,
    SomeExplicit = 1 << 3,
    Explicit = 1 << 4,
    Abridged = 1 << 5,
    Lgbt = 1 << 6,
}
