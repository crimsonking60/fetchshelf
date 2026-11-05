namespace Fetchshelf.Clients.Hardcover;

/// <summary>Hardcover refused or failed a request; the message says why in plain words.</summary>
public class HardcoverException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Hardcover rejected the token: it is wrong or has expired (HTTP 401), or lacks a permission the request needs
/// (HTTP 403).
/// </summary>
public sealed class HardcoverAuthenticationException(string message) : HardcoverException(message);
