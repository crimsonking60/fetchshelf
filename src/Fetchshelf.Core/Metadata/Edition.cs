namespace Fetchshelf.Core.Metadata;

/// <summary>
/// A book edition: a display label (e.g. "2nd Edition") plus a number (ordinal position, or 0 when
/// the edition has no discernible number, such as "UK Edition").
/// </summary>
public sealed record Edition(string Label, int Number);
