namespace Fetchshelf.Tests.Scale;

/// <summary>
/// A timing test over a large seeded library. Skipped unless `FETCHSHELF_SCALE` holds the number of torrents to
/// seed, so the normal test run stays fast:
/// <code>FETCHSHELF_SCALE=10000 dotnet test --filter "Category=Scale" --logger "console;verbosity=detailed"</code>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ScaleFactAttribute : FactAttribute
{
    public ScaleFactAttribute()
    {
        if (TorrentCount is null)
        {
            Skip = "Set FETCHSHELF_SCALE to the number of torrents to seed to run the scale tests.";
        }
    }

    /// <summary>The number of torrents to seed, from `FETCHSHELF_SCALE`; null when it is unset or not a positive number.</summary>
    public static int? TorrentCount =>
        int.TryParse(Environment.GetEnvironmentVariable("FETCHSHELF_SCALE"), out var count) && count > 0 ? count : null;
}
