using System.Reflection;

namespace Fetchshelf.Web;

/// <summary>
/// The running version: a release number such as <c>2026.10.2</c>, <c>edge-&lt;commit&gt;</c> for edge images,
/// or <c>dev</c> for a local build. Bug reports quote it, so it is shown on the Config page and logged at startup.
/// </summary>
public static class AppVersion
{
    public static string Current { get; } = FromInformationalVersion(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>The informational version without the <c>+&lt;commit&gt;</c> suffix the SDK adds in a Git checkout.</summary>
    public static string FromInformationalVersion(string? informationalVersion) =>
        string.IsNullOrWhiteSpace(informationalVersion) ? "unknown" : informationalVersion.Split('+')[0];
}
