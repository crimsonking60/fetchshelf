namespace Fetchshelf.Clients.Covers;

/// <summary>
/// The service refused, asked to slow down (429) or failed (5xx): nothing more is asked of it for a while, and the books
/// it would have been asked about wait.
/// </summary>
public sealed class CoverServiceRefusedException(string message) : Exception(message);
