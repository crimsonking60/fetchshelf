using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Organizer;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// Everything the host builds from its container resolves: every runner, every hosted service and every page model, for
/// a configuration with every optional part on, and for a minimal one. A registration that goes missing or takes the
/// wrong value fails here rather than in the first job run or page view.
/// </summary>
public sealed class ContainerResolutionTests
{
    private const string Everything = """
        mam_id = "test-mam-id"
        min_ratio = 1.5
        add_torrents_stopped = true
        exclude_narrator_in_library_dir = true
        covers = 20

        [[qbittorrent]]
        url = "http://qbittorrent.test"
        username = "admin"
        password = "adminadmin"

        [[library]]
        name = "Audiobooks"
        category = "audiobooks"
        library_dir = "/library/audiobooks"

        [[autograb]]
        name = "Bookmarks"
        type = "bookmarks"

        [hardcover]
        token = "hardcover-token"

        [[hardcover_list]]
        status = "want_to_read"

        [[hardcover_list.grab]]
        cost = "free"

        [follow]

        [[follow.grab]]
        cost = "free"

        [upgrade]

        [bonus]
        dry_run = true

        [backup]
        folder = "backups"
        interval = 0

        [audiobookshelf]
        url = "http://abs.test"
        token = "abs-token"

        [audiobookshelf.path_mapping]
        "/abs" = "/library/audiobooks"

        [opds]

        [[tag]]
        category = "audiobooks"

        [[notify]]
        url = "http://notify.test/topic"
        format = "ntfy"
        events = ["all"]
        """;

    private static readonly Type[] Runners =
    [
        typeof(LinkRunner), typeof(Cleaner), typeof(ClientStatusChecker), typeof(DownloadProgressChecker), typeof(AbsMatcher),
        typeof(SelectionRunner), typeof(AutograbberRunner), typeof(ListImporter),
        typeof(FollowRunner), typeof(FollowService), typeof(UpgradeRunner), typeof(BonusRunner), typeof(DownloadRunner),
        typeof(RetroactiveTagApplier),
    ];

    private static IEnumerable<Type> PageModels() =>
        typeof(Program).Assembly.GetTypes().Where(type => type is { IsAbstract: false, IsClass: true } && type.IsSubclassOf(typeof(PageModel)));

    private static FetchshelfWebAppFixture Fixture(string config) =>
        new(config, notifyHandler: _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));

    [Fact]
    public async Task EverythingConfigured_EveryRunnerHostedServiceAndPageModelResolves()
    {
        using var fixture = Fixture(Everything);
        await fixture.CreateClient().GetAsync("/health");
        using var scope = fixture.CreateScope();

        Assert.All(Runners, runner => Assert.NotNull(scope.ServiceProvider.GetRequiredService(runner)));
        Assert.NotEmpty(scope.ServiceProvider.GetServices<IHostedService>());
        Assert.All(PageModels(), page => Assert.NotNull(ActivatorUtilities.CreateInstance(scope.ServiceProvider, page)));
    }

    [Fact]
    public async Task OptionRecords_CarryTheConfiguredValues()
    {
        using var fixture = Fixture(Everything);
        await fixture.CreateClient().GetAsync("/health");
        using var scope = fixture.CreateScope();

        Assert.Equal(new DownloadRunnerOptions(MinRatio: 1.5, AddTorrentsStopped: true), scope.ServiceProvider.GetRequiredService<DownloadRunnerOptions>());
        Assert.True(scope.ServiceProvider.GetRequiredService<LinkRunnerOptions>().ExcludeNarratorInLibraryDir);
        Assert.Equal("/abs", scope.ServiceProvider.GetRequiredService<AbsMatcherOptions>().FetchshelfToAbsPaths["/library/audiobooks"]);
        Assert.Equal("Audiobooks", Assert.Single(scope.ServiceProvider.GetRequiredService<IReadOnlyList<Fetchshelf.Data.Configuration.Library>>()).Name);
        Assert.Single(scope.ServiceProvider.GetRequiredService<IReadOnlyList<Fetchshelf.Core.AutoDownloader.TagRule>>());
        Assert.Single(scope.ServiceProvider.GetRequiredService<IReadOnlyList<Fetchshelf.Clients.QBittorrent.QBittorrentClient>>());
        Assert.IsType<Fetchshelf.Data.Organizer.ChainedCoverSource>(scope.ServiceProvider.GetRequiredService<Fetchshelf.Data.Organizer.ICoverSource>());
        Assert.Equal(20, scope.ServiceProvider.GetRequiredService<Fetchshelf.Data.Organizer.CoverWriterOptions>().MaxFetchesPerRun);
    }

    [Fact]
    public async Task MinimalConfiguration_EveryPageModelResolves()
    {
        using var fixture = Fixture("mam_id = \"test-mam-id\"\n");
        await fixture.CreateClient().GetAsync("/health");
        using var scope = fixture.CreateScope();

        Assert.All(PageModels(), page => Assert.NotNull(ActivatorUtilities.CreateInstance(scope.ServiceProvider, page)));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<LinkRunner>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DownloadRunner>());
        // Without covers, no cover source: nothing is asked of Open Library or Audnexus.
        Assert.Null(scope.ServiceProvider.GetService<Fetchshelf.Data.Organizer.ICoverSource>());
    }

    [Fact]
    public async Task NoMamId_EveryPageModelResolves()
    {
        using var fixture = Fixture("mam_id = \"\"\n");
        await fixture.CreateClient().GetAsync("/health");
        using var scope = fixture.CreateScope();

        Assert.All(PageModels(), page => Assert.NotNull(ActivatorUtilities.CreateInstance(scope.ServiceProvider, page)));
    }

    [Fact]
    public async Task EverythingConfigured_RegistersEveryJob()
    {
        using var fixture = Fixture(Everything);
        await fixture.CreateClient().GetAsync("/health");

        Assert.Equal(
            ["Audiobookshelf", "Backup", "Bonus", "Bookmarks", "Clean", "Download", "Follow", "hardcover_list[0]", "Library check", "Link", "Upgrade"],
            fixture.JobRegistry.GetAll().Select(job => job.Name).Order(StringComparer.OrdinalIgnoreCase));
    }
}
