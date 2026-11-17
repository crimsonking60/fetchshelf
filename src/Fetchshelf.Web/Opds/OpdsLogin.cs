using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Opds;

/// <summary>
/// The `[opds]` login: when a username and password are set, every `/opds` request must carry them as HTTP Basic, which
/// every OPDS reader supports. Only the catalog asks for it; the rest of Fetchshelf stays without a login.
/// </summary>
public sealed class OpdsLogin(OpdsSettings settings, ILogger<OpdsLogin> logger) : IEndpointFilter
{
    public const string Realm = "Fetchshelf OPDS";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (Matches(http.Request.Headers.Authorization.ToString()))
        {
            return await next(context);
        }

        if (http.Request.Headers.Authorization.Count > 0)
        {
            // What was sent is never logged: it may be the password with a typo.
            logger.LogInformation("Wrong OPDS login from {RemoteAddress}", http.Connection.RemoteIpAddress);
        }

        http.Response.Headers.WWWAuthenticate = $"Basic realm=\"{Realm}\", charset=\"UTF-8\"";
        return Results.Unauthorized();
    }

    /// <summary>
    /// Whether <paramref name="authorization"/> is HTTP Basic with the configured username and password. Compared as
    /// hashes in fixed time, so the answer's timing says nothing about how much of the login was right.
    /// </summary>
    public bool Matches(string? authorization)
    {
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || header.Parameter is null)
        {
            return false;
        }

        byte[] sent;
        try
        {
            sent = Convert.FromBase64String(header.Parameter);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}");
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(sent), SHA256.HashData(expected));
    }
}
