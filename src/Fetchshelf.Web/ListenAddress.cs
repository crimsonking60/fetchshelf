using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web;

/// <summary>Builds the Kestrel listen URL from the configured `web_host`/`web_port` (Fetchshelf-spec.md §4.1, §10).</summary>
public static class ListenAddress
{
    public static string Build(FetchshelfSettings settings) => $"http://{settings.WebHost}:{settings.WebPort}";
}
