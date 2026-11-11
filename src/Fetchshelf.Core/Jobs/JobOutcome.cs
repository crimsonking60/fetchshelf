namespace Fetchshelf.Core.Jobs;

/// <summary>What a job run did, as its delegate reports it.</summary>
/// <param name="Summary">One readable line, such as "3 torrents linked".</param>
/// <param name="Counts">The same numbers by name (<c>linked</c>, <c>failed</c>, ...), for the run history.</param>
public sealed record JobOutcome(string Summary, IReadOnlyDictionary<string, long>? Counts = null);
