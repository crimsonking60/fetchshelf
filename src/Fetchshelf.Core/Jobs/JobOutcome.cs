namespace Fetchshelf.Core.Jobs;

/// <summary>What a job run did, as its delegate reports it.</summary>
/// <param name="Summary">One readable line, such as "3 torrents linked".</param>
/// <param name="Counts">The same numbers by name (<c>linked</c>, <c>failed</c>, ...), for the run history.</param>
/// <param name="Details">
/// Lines on what the run did one by one, such as each torrent it selected, kept with the run and shown under it in the
/// job's history. Null when there is nothing to list.
/// </param>
public sealed record JobOutcome(string Summary, IReadOnlyDictionary<string, long>? Counts = null, IReadOnlyList<string>? Details = null);
