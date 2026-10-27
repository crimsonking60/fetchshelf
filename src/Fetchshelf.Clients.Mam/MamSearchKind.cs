namespace Fetchshelf.Clients.Mam;

/// <summary>The "kind" filter (e.g. freeleech, free-or-VIP), Fetchshelf-spec.md §5.1.</summary>
public enum MamSearchKind
{
    Freeleech,
    FreeOrVip,
    Vip,
}

internal static class MamSearchKindExtensions
{
    public static string ToWireValue(this MamSearchKind kind) => kind switch
    {
        MamSearchKind.Freeleech => "fl",
        MamSearchKind.FreeOrVip => "fl-VIP",
        MamSearchKind.Vip => "VIP",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
