using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Hosting;

/// <summary>
/// Where Fetchshelf keeps its files: the configuration file, the database, and the folder of the rolling log file.
/// </summary>
/// <param name="ConfigFile">`FETCHSHELF_CONFIG_FILE`, or `config.toml`.</param>
/// <param name="DataFile">`FETCHSHELF_DATA_FILE`, or `data.db`.</param>
/// <param name="LogFolder">`FETCHSHELF_LOG_DIR`; null or empty means no log file, only the console.</param>
public sealed record FetchshelfPaths(string ConfigFile, string DataFile, string? LogFolder)
{
    /// <summary>
    /// The folder the database is in, where the form keys, the instance id and the OPDS covers live too, and which a
    /// relative `[backup]` folder is under.
    /// </summary>
    public string DataFolder => Path.GetDirectoryName(Path.GetFullPath(DataFile)) ?? ".";

    /// <summary>The paths as the environment variables set them.</summary>
    /// <param name="getEnv">Reads an environment variable; <see cref="Environment.GetEnvironmentVariable(string)"/> outside tests.</param>
    /// <param name="defaultDirectory">
    /// Where the configuration file and the database go when their variables aren't set. Null means the working
    /// directory.
    /// </param>
    public static FetchshelfPaths Resolve(Func<string, string?> getEnv, string? defaultDirectory = null) => new(
        getEnv("FETCHSHELF_CONFIG_FILE") ?? ConfigLoader.ResolveDefaultConfigFilePath(defaultDirectory),
        getEnv("FETCHSHELF_DATA_FILE") ?? (defaultDirectory is null ? "data.db" : Path.Combine(defaultDirectory, "data.db")),
        getEnv("FETCHSHELF_LOG_DIR"));

    /// <summary>
    /// The paths from a <c>--FETCHSHELF_CONFIG_FILE=…</c>, <c>--FETCHSHELF_DATA_FILE=…</c> or <c>--FETCHSHELF_LOG_DIR=…</c>
    /// argument where one is given, else from the environment. The web tests' hosts get theirs as arguments
    /// (<c>WebApplicationFactory</c> hands its settings to the program that way), so hosts built side by side never
    /// share them.
    /// </summary>
    public static FetchshelfPaths FromArgumentsOrEnvironment(IReadOnlyList<string> args) =>
        Resolve(name => Argument(args, name) ?? Environment.GetEnvironmentVariable(name));

    private static string? Argument(IReadOnlyList<string> args, string name)
    {
        var prefix = "--" + name + "=";
        return args.LastOrDefault(arg => arg.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }
}
