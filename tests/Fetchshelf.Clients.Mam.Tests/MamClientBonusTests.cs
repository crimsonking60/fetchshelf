using System.Net;
using System.Text;

namespace Fetchshelf.Clients.Mam.Tests;

/// <summary>Purchases with bonus points. The answers are ones MAM gave, from a year of purchases by a shell script.</summary>
public class MamClientBonusTests
{
    private const string WedgeBought =
        """{"success":true,"type":"wedge","amount":1,"method":"points","seedbonus":9421.163999999997,"FLleft":369}""";

    private const string RateLimited = """{"success":false,"error":"Rate Limit Exceeded."}""";

    private readonly ManualTimeProvider _time = new();
    private readonly List<TimeSpan> _delays = [];
    private readonly List<string> _urls = [];

    private MamClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(
        new HttpClient(new FakeHttpMessageHandler(request =>
        {
            _urls.Add(request.RequestUri!.ToString());
            return handler(request);
        })),
        new MamClientOptions { MamId = "cookie" },
        _time,
        (wait, _) =>
        {
            _delays.Add(wait);
            _time.Advance(wait);
            return Task.CompletedTask;
        });

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(MamBonusSpend.Upload, 50, "spendtype=upload&amount=50&_=")]
    [InlineData(MamBonusSpend.Wedge, 0, "spendtype=wedges&source=points&_=")]
    [InlineData(MamBonusSpend.Vip, 0, "spendtype=VIP&duration=max&_=")]
    public async Task BuyWithBonusPointsAsync_SendsTheSpendTypeToBonusBuy(MamBonusSpend spend, int gb, string query)
    {
        var client = ClientFor(_ => Ok("""{"success":true,"seedbonus":100}"""));

        await client.BuyWithBonusPointsAsync(spend, gb);

        var url = Assert.Single(_urls);
        Assert.StartsWith($"https://www.myanonamouse.net/json/bonusBuy.php/?{query}", url, StringComparison.Ordinal);
        Assert.EndsWith(_time.GetUtcNow().ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture), url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuyWithBonusPointsAsync_WedgeBought_ReadsThePointsAndWedgesLeft()
    {
        var client = ClientFor(_ => Ok(WedgeBought));

        var result = await client.BuyWithBonusPointsAsync(MamBonusSpend.Wedge);

        Assert.Equal(new MamBonusPurchaseResult(true, 9421.163999999997, null, 369), result);
        Assert.False(result.IsRateLimited);
    }

    [Fact]
    public async Task BuyWithBonusPointsAsync_Refused_ReadsTheReason()
    {
        var client = ClientFor(_ => Ok(RateLimited));

        var result = await client.BuyWithBonusPointsAsync(MamBonusSpend.Upload, 50);

        Assert.Equal(new MamBonusPurchaseResult(false, null, "Rate Limit Exceeded.", null), result);
        Assert.True(result.IsRateLimited);
    }

    [Theory]
    [InlineData("<html>Log in</html>")]
    [InlineData("""{"seedbonus":100}""")]
    public async Task BuyWithBonusPointsAsync_AnswerIsNotAPurchaseAnswer_Throws(string body)
    {
        var client = ClientFor(_ => Ok(body));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.BuyWithBonusPointsAsync(MamBonusSpend.Upload, 1));

        Assert.Contains(body, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuyWithBonusPointsAsync_NoAnswer_IsNotSentAgain()
    {
        var client = ClientFor(_ => throw new HttpRequestException("Connection reset by peer"));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.BuyWithBonusPointsAsync(MamBonusSpend.Upload, 50));

        Assert.Single(_urls);
    }

    [Fact]
    public async Task BuyWithBonusPointsAsync_TwoPurchases_AreTenSecondsApart()
    {
        var client = ClientFor(_ => Ok("""{"success":true,"seedbonus":100}"""));

        await client.BuyWithBonusPointsAsync(MamBonusSpend.Upload, 50);
        _time.Advance(TimeSpan.FromSeconds(3));
        await client.BuyWithBonusPointsAsync(MamBonusSpend.Upload, 50);

        Assert.Equal([TimeSpan.FromSeconds(7)], _delays);
    }
}
