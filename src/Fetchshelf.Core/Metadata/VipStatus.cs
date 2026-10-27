namespace Fetchshelf.Core.Metadata;

/// <summary>VIP / freeleech status of a torrent, Fetchshelf-spec.md §2 ("Freeleech / VIP") and §3.1.</summary>
public enum VipStatusKind
{
    NotVip,
    Permanent,
    Temporary,
    Unknown,
}

/// <summary>
/// A torrent's VIP status. <see cref="ExpiresOn"/> is only meaningful when <see cref="Kind"/> is
/// <see cref="VipStatusKind.Temporary"/>.
/// </summary>
public sealed record VipStatus(VipStatusKind Kind, DateOnly? ExpiresOn = null)
{
    public static readonly VipStatus NotVip = new(VipStatusKind.NotVip);
    public static readonly VipStatus Permanent = new(VipStatusKind.Permanent);
    public static readonly VipStatus Unknown = new(VipStatusKind.Unknown);

    public static VipStatus Temporary(DateOnly expiresOn) => new(VipStatusKind.Temporary, expiresOn);
}
