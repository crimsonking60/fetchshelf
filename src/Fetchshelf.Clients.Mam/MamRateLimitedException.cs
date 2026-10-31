namespace Fetchshelf.Clients.Mam;

/// <summary>Thrown when MAM responds with HTTP 429 (rate limited).</summary>
public sealed class MamRateLimitedException(string message) : Exception(message);
