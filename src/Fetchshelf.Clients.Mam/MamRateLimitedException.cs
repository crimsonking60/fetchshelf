namespace Fetchshelf.Clients.Mam;

/// <summary>Thrown when MaM responds with HTTP 429 (rate limited), Fetchshelf-spec.md §5.1.</summary>
public sealed class MamRateLimitedException(string message) : Exception(message);
