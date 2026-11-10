using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web;

/// <summary>
/// <c>dotnet Fetchshelf.Web.dll --check-config</c>: loads the configuration exactly as startup would, environment
/// overrides included, and prints what it understood (0) or why it would not start (1). It contacts no service,
/// leaves the database alone, and doesn't create a missing file, so it is safe to run next to the running instance.
/// </summary>
public static class ConfigCheck
{
    public const string Argument = "--check-config";

    public static int Run(string configFilePath, TextWriter output)
    {
        if (!File.Exists(configFilePath))
        {
            output.WriteLine($"No configuration file at {configFilePath}. On its first start, Fetchshelf creates one with an empty mam_id.");
            return 1;
        }

        try
        {
            var settings = ConfigLoader.Load(configFilePath);
            var mamIdFromEnvironment = Environment.GetEnvironmentVariable("FETCHSHELF_CONF_mam_id") is not null;
            output.Write(ConfigSummary.Build(settings, configFilePath, mamIdFromEnvironment));
            return 0;
        }
        catch (ConfigurationException ex)
        {
            output.WriteLine($"Fetchshelf would not start with {configFilePath}: {ex.Message}");
            return 1;
        }
    }
}
