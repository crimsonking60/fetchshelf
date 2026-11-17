using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Hosting;

/// <summary>The host names Fetchshelf answers to, from `allowed_hosts`, as ASP.NET Core's host filtering takes them.</summary>
public static class AllowedHosts
{
    /// <summary>The names local access and the container's health probe use, always allowed along with `allowed_hosts`.</summary>
    public static readonly IReadOnlyList<string> Local = ["localhost", "127.0.0.1", "[::1]"];

    /// <summary>
    /// `allowed_hosts`, the local names and the host the health probe calls (`web_host` when it is one address), separated
    /// by semicolons; null when `allowed_hosts` is empty and any host is answered.
    /// </summary>
    public static string? For(FetchshelfSettings settings)
    {
        if (settings.AllowedHosts.Count == 0)
        {
            return null;
        }

        // The host the health probe asks, worked out the same way the probe does.
        var probeHost = new Uri(HealthProbe.ProbeUrl(settings)).Host;
        return string.Join(';', settings.AllowedHosts.Concat(Local).Append(probeHost).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
