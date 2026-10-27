namespace Fetchshelf.Clients.Mam;

/// <summary>
/// MaM answered HTTP 403. In practice that means the configured `mam_id` was invalidated or is locked to another IP,
/// or the account or address is blocked.
/// </summary>
public sealed class MamForbiddenException(string message) : Exception(message);
