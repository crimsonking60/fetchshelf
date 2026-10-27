namespace Fetchshelf.Clients.Mam;

/// <summary>Thrown when the configured `mam_id` is rejected by MaM.</summary>
public sealed class MamAuthenticationException(string message) : Exception(message);
