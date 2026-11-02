namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Thrown when MAM responds with HTTP 406 (account temporarily blocked); any in-progress download
/// run MUST abort on it rather than continuing with further MAM requests.
/// </summary>
public sealed class MamAccountBlockedException(string message) : Exception(message);
