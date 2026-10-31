namespace Fetchshelf.Clients.Mam;

/// <summary>Thrown when the configured `mam_id` is rejected by MAM.</summary>
public sealed class MamAuthenticationException(string message) : Exception(message);
