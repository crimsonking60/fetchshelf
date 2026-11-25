namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Thrown when MAM answers a search with "Search returned an error": it couldn't read the search text, typically
/// because a character in it is one of its search operators (a search of nothing but "not", for instance).
/// <see cref="MamReason"/> is MAM's own explanation as plain text.
/// </summary>
public sealed class MamSearchRefusedException(string mamReason)
    : InvalidOperationException($"MAM search failed: MAM could not read the search: {mamReason}")
{
    /// <summary>MAM's explanation, without its HTML.</summary>
    public string MamReason { get; } = mamReason;
}
