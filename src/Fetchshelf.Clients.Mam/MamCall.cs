namespace Fetchshelf.Clients.Mam;

/// <summary>
/// One request sent to MAM, as <see cref="MamClient.CallCompleted"/> reports it: each attempt is one call, so a request
/// sent again after a 503 is two. A request that a pause stopped before it was sent is no call.
/// </summary>
/// <param name="StartedAt">When the request was sent, after any wait for a rate limit to pass.</param>
/// <param name="Duration">How long until the whole answer was read, or until the request failed.</param>
/// <param name="Method">The HTTP method, such as GET.</param>
/// <param name="Host">The host the request went to: MAM's site, or its CDN for a cover.</param>
/// <param name="Path">The path, without the query; a download's personal token is replaced by <c>&lt;hash&gt;</c>.</param>
/// <param name="StatusCode">The HTTP status of the answer; null when there was none.</param>
/// <param name="Failure">Why there was no answer, such as "No answer in time"; null when there was one.</param>
public sealed record MamCall(
    DateTimeOffset StartedAt, TimeSpan Duration, string Method, string Host, string Path, int? StatusCode, string? Failure);
