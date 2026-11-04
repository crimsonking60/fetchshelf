namespace Fetchshelf.Clients.Hardcover.Tests;

/// <summary>Routes requests to a handler function, so the Hardcover client can be tested without a real server.</summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(handler(request));
}
