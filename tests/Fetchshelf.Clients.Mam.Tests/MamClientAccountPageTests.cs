using System.Net;
using Fetchshelf.Tests.Shared;

namespace Fetchshelf.Clients.Mam.Tests;

/// <summary>What the Account page reads from <see cref="MamClient"/>: the account's details and the client's own state.</summary>
public class MamClientAccountPageTests
{
    private const string EmptyResultJson = """{"perpage":0,"start":0,"total":0,"found":0,"data":[]}""";

    private const string MinimalAccountJson = """
        {"ratio": 5, "uploaded_bytes": 100, "downloaded_bytes": 20, "wedges": 2, "uid": 1,
         "snatch_summary": {"unsat": {"count": 3, "limit": 50}}}
        """;

    private static MamClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> handler, ManualTimeProvider? time = null) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), new MamClientOptions { MamId = "cookie" }, time,
            delay: (_, _) => Task.CompletedTask);

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    [Fact]
    public async Task GetAccountStateAsync_RealAnswer_ReadsTheAccountsDetails()
    {
        var client = ClientFor(_ => Ok(MamAccountFixture.Json));

        var state = await client.GetAccountStateAsync();

        Assert.Equal(("example-user", "Power User", 41250.0, "yes"), (state.Username, state.ClassName, state.BonusPoints, state.Connectable));
        Assert.Equal((14, 20, 37), (state.UnsatCount, state.UnsatLimit, state.Wedges));
    }

    [Fact]
    public async Task GetAccountStateAsync_DetailsMissing_LeavesThemEmpty()
    {
        var client = ClientFor(_ => Ok(MinimalAccountJson));

        var state = await client.GetAccountStateAsync();

        Assert.Null(state.Username);
        Assert.Null(state.ClassName);
        Assert.Null(state.BonusPoints);
        Assert.Null(state.Connectable);
        Assert.Equal("unsat", Assert.Single(state.SnatchKinds).Key);
    }

    [Fact]
    public async Task GetAccountStateAsync_SnatchSummary_ReadsEveryKindInOrderAndSkipsFlatValues()
    {
        var client = ClientFor(_ => Ok(MamAccountFixture.Json));

        var kinds = (await client.GetAccountStateAsync()).SnatchKinds;

        Assert.Equal(
            ["duplicates", "inactHnr", "inactSat", "inactUnsat", "ite", "leeching", "reseed", "sSat", "seedHnr", "seedUnsat", "unsat", "upAct", "upInact"],
            kinds.Select(k => k.Key));
        Assert.Equal(new MamSnatchKind("sSat", "Seeding - Satisfied", 230, 912680550400), kinds.Single(k => k.Key == "sSat"));
        Assert.Null(kinds.Single(k => k.Key == "inactHnr").SizeBytes); // size null
        Assert.Null(kinds.Single(k => k.Key == "duplicates").SizeBytes); // no size at all
        Assert.Equal("Seeding -  Uploads", kinds.Single(k => k.Key == "upAct").Name); // as MAM wrote it
    }

    [Fact]
    public async Task LastAccountState_AfterTheCacheRunsOut_IsStillTheLastRead()
    {
        var time = new ManualTimeProvider();
        var client = ClientFor(_ => Ok(MamAccountFixture.Json), time);
        Assert.Null(client.LastAccountState);
        var readAt = time.GetUtcNow();

        await client.GetAccountStateAsync();
        time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(readAt, client.LastAccountState!.FetchedAt);
        Assert.Equal(37, client.LastAccountState.State.Wedges);
    }

    [Fact]
    public async Task RefreshAccountStateAsync_WithinTheCacheWindow_ReadsMamAgain()
    {
        var requests = 0;
        var client = ClientFor(_ =>
        {
            requests++;
            return Ok(MamAccountFixture.Json);
        });

        await client.GetAccountStateAsync();
        await client.RefreshAccountStateAsync();

        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task PausedUntil_AfterA403_IsSetUntilThePauseEnds()
    {
        var time = new ManualTimeProvider();
        var client = ClientFor(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), time);

        await Assert.ThrowsAsync<MamForbiddenException>(() => client.SearchAsync(new MamSearchQuery()));

        Assert.Equal(time.GetUtcNow().AddMinutes(30), client.PausedUntil);
        Assert.Equal(MamPauseReason.Forbidden, client.PauseReason);
        Assert.Contains("HTTP 403", client.LastRefusal!.Message);
        Assert.Equal(time.GetUtcNow(), client.LastRefusal.At);
        time.Advance(TimeSpan.FromMinutes(31));
        Assert.Null(client.PausedUntil);
        Assert.Null(client.PauseReason);
        Assert.NotNull(client.LastRefusal); // the refusal itself is still worth showing
    }

    [Fact]
    public async Task PausedUntil_AfterA429_IsSetUntilTheWaitEnds()
    {
        var time = new ManualTimeProvider();
        var client = ClientFor(_ => new HttpResponseMessage((HttpStatusCode)429), time);

        await Assert.ThrowsAsync<MamRateLimitedException>(() => client.SearchAsync(new MamSearchQuery()));

        Assert.Equal((time.GetUtcNow().AddMinutes(1), MamPauseReason.RateLimited), (client.PausedUntil, client.PauseReason));
        Assert.Null(client.LastRefusal); // a rate limit refuses neither the session nor the account
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(client.PausedUntil);
    }

    [Fact]
    public async Task LastRefusal_After406_SaysTheAccountIsBlocked()
    {
        var client = ClientFor(_ => new HttpResponseMessage((HttpStatusCode)406));

        await Assert.ThrowsAsync<MamAccountBlockedException>(() => client.SearchAsync(new MamSearchQuery()));

        Assert.Contains("HTTP 406", client.LastRefusal!.Message);
        Assert.Null(client.PausedUntil);
    }

    [Fact]
    public async Task LastSuccessAt_AfterASuccessfulSearch_IsWhenItAnswered()
    {
        var time = new ManualTimeProvider();
        var client = ClientFor(_ => Ok(EmptyResultJson), time);
        Assert.Null(client.LastSuccessAt);

        await client.SearchAsync(new MamSearchQuery());

        Assert.Equal(time.GetUtcNow(), client.LastSuccessAt);
    }
}
