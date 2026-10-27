namespace Fetchshelf.Clients.Mam;

/// <summary>Snatch list kinds, Fetchshelf-spec.md §5.1.</summary>
public enum MamSnatchlistKind
{
    Unsat,
    InactiveUnsat,
    SeedingUnsat,
    SeedingSatisfied,
    InactiveSatisfied,
    ActiveUploads,
}

internal static class MamSnatchlistKindExtensions
{
    public static string ToWireValue(this MamSnatchlistKind kind) => kind switch
    {
        MamSnatchlistKind.Unsat => "unsat",
        MamSnatchlistKind.InactiveUnsat => "inactUnsat",
        MamSnatchlistKind.SeedingUnsat => "seedUnsat",
        MamSnatchlistKind.SeedingSatisfied => "sSat",
        MamSnatchlistKind.InactiveSatisfied => "inactSat",
        MamSnatchlistKind.ActiveUploads => "upAct",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
