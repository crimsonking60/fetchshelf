using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Organizer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// Boots the real Fetchshelf.Web host against an isolated temp config file, with a
/// test-controlled <see cref="JobRegistry"/> swapped in so tests can register jobs before
/// requesting pages.
/// </summary>
public sealed class FetchshelfWebAppFixture : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-web-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>The registry the host uses, unless the fixture was made with <c>realJobRegistry</c>: then the app's own is in use.</summary>
    public JobRegistry JobRegistry { get; } = new(classifyFailure: Fetchshelf.Web.Jobs.JobFailureClassifier.Classify);

    /// <summary>The folder holding the test's configuration and, by default, its database.</summary>
    public string DataDirectory => _tempDir;

    public FetchshelfWebAppFixture(
        string configToml = "mam_id = \"\"\n",
        IMetadataLookup? metadataLookup = null,
        Func<HttpRequestMessage, HttpResponseMessage>? qbittorrentHandler = null,
        Func<HttpRequestMessage, HttpResponseMessage>? mamHandler = null,
        Func<HttpRequestMessage, HttpResponseMessage>? absHandler = null,
        string dataFileRelativePath = "data.db",
        Func<HttpRequestMessage, HttpResponseMessage>? notifyHandler = null,
        bool realJobRegistry = false,
        Action<IServiceCollection>? configureServices = null)
    {
        var configPath = Path.Combine(_tempDir, "config.toml");
        File.WriteAllText(configPath, configToml);
        Environment.SetEnvironmentVariable("FETCHSHELF_CONFIG_FILE", configPath);
        Environment.SetEnvironmentVariable("FETCHSHELF_DATA_FILE", Path.Combine(_tempDir, dataFileRelativePath));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                if (!realJobRegistry)
                {
                    services.AddSingleton<IJobRegistry>(JobRegistry);
                }

                // Never let a test reach a real notification service: the URL could be someone's real topic. Tests that
                // expect messages pass their own notifyHandler; without one, nothing is sent anywhere.
                services.AddHttpClient("notify")
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(notifyHandler ?? (request =>
                        throw new InvalidOperationException(
                            $"Test sent a real notification ({request.Method} {request.RequestUri?.Host}). Pass notifyHandler to FetchshelfWebAppFixture."))));

                configureServices?.Invoke(services);

                if (metadataLookup is not null)
                {
                    services.AddSingleton(metadataLookup);
                }

                if (qbittorrentHandler is not null)
                {
                    services.AddHttpClient("qbittorrent")
                        .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(qbittorrentHandler));
                }

                // Never let a test reach the real MAM: a request from here would carry this machine's IP and an
                // empty or placeholder mam_id, which can get the real session or address rejected. Tests that
                // expect MAM calls must pass their own mamHandler.
                services.AddHttpClient("mam")
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(mamHandler ?? (request =>
                        throw new InvalidOperationException(
                            $"Test sent a real MAM request ({request.Method} {request.RequestUri?.AbsolutePath}). Pass mamHandler to FetchshelfWebAppFixture."))));

                if (absHandler is not null)
                {
                    services.AddHttpClient("abs")
                        .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(absHandler));
                }
            }));
    }

    public HttpClient CreateClient() => _factory.CreateClient();

    public HttpClient CreateNoRedirectClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public IServiceScope CreateScope() => _factory.Services.CreateScope();

    public void Dispose()
    {
        _factory.Dispose();
        Environment.SetEnvironmentVariable("FETCHSHELF_CONFIG_FILE", null);
        Environment.SetEnvironmentVariable("FETCHSHELF_DATA_FILE", null);
        Directory.Delete(_tempDir, recursive: true);
    }
}
