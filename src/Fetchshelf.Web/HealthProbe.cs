using System.Net;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web;

/// <summary>
/// The container health check, run as <c>dotnet Fetchshelf.Web.dll --healthcheck</c>: asks the running instance's
/// <c>/health</c> endpoint and returns 0 when it answers healthy, 1 otherwise. The port comes from the same
/// configuration the running instance read, so a changed <c>web_port</c> is followed without extra setup.
/// </summary>
public static class HealthProbe
{
    public const string Argument = "--healthcheck";

    public static async Task<int> RunAsync(string configFilePath, TimeSpan timeout)
    {
        // The running instance creates the file at startup; without it there is nothing to ask, and creating it here
        // (as whichever user runs the check) is not this probe's job.
        if (!File.Exists(configFilePath))
        {
            Console.WriteLine($"unhealthy: no configuration file at {configFilePath}");
            return 1;
        }

        try
        {
            var url = ProbeUrl(ConfigLoader.Load(configFilePath));
            using var httpClient = new HttpClient { Timeout = timeout };
            using var response = await httpClient.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"{(int)response.StatusCode} {body}");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unhealthy: {ex.Message}");
            return 1;
        }
    }

    /// <summary>The <c>/health</c> URL for the configured listen address; a wildcard host is asked on the loopback address.</summary>
    public static string ProbeUrl(FetchshelfSettings settings)
    {
        var host = settings.WebHost switch
        {
            "" or "0.0.0.0" or "*" or "+" => "127.0.0.1",
            "::" or "[::]" => "[::1]",
            var configured when IPAddress.TryParse(configured, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                => $"[{configured}]",
            var configured => configured,
        };
        return $"http://{host}:{settings.WebPort}/health";
    }
}
