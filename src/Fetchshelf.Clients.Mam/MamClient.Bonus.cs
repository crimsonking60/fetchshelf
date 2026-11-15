using System.Globalization;
using System.Text.Json;

namespace Fetchshelf.Clients.Mam;

/// <summary>What <see cref="MamClient.BuyWithBonusPointsAsync"/> buys.</summary>
public enum MamBonusSpend
{
    /// <summary>Upload credit, in whole GB.</summary>
    Upload,

    /// <summary>One freeleech wedge.</summary>
    Wedge,

    /// <summary>VIP, topped up to the most MAM allows.</summary>
    Vip,
}

/// <summary>MAM's answer to a purchase with bonus points.</summary>
/// <param name="Success">Whether MAM made the purchase.</param>
/// <param name="PointsAfter">The points left, which MAM gives on success; null when it gave none.</param>
/// <param name="Error">MAM's reason for refusing, such as "Rate Limit Exceeded."; null on success.</param>
/// <param name="WedgesAfter">For a wedge, the wedges the account holds now; null otherwise.</param>
public sealed record MamBonusPurchaseResult(bool Success, double? PointsAfter, string? Error, long? WedgesAfter)
{
    /// <summary>Whether MAM refused because purchases came too fast.</summary>
    public bool IsRateLimited => Error?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true;
}

public sealed partial class MamClient
{
    /// <summary>How long after one purchase the next is sent at the earliest. Purchases in quick succession are refused.</summary>
    private static readonly TimeSpan BonusPurchasePoliteness = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _bonusPurchasePoliteness = new(1, 1);
    private DateTimeOffset? _lastBonusPurchaseAt;

    /// <summary>
    /// Buys with the account's bonus points through `GET /json/bonusBuy.php/?spendtype=…`. Never sent twice: a request
    /// that gets no answer may have reached MAM, so it isn't tried again. Purchases are at least 10 seconds apart.
    /// </summary>
    /// <param name="spend">What to buy.</param>
    /// <param name="uploadGb">For <see cref="MamBonusSpend.Upload"/>, how many GB; ignored otherwise.</param>
    /// <param name="cancellationToken">Cancels the wait and the request.</param>
    /// <exception cref="InvalidOperationException">MAM's answer isn't the JSON a purchase gives.</exception>
    public async Task<MamBonusPurchaseResult> BuyWithBonusPointsAsync(
        MamBonusSpend spend, int uploadGb = 0, CancellationToken cancellationToken = default)
    {
        await ApplyBonusPurchasePolitenessAsync(cancellationToken);

        var query = spend switch
        {
            MamBonusSpend.Upload => $"spendtype=upload&amount={uploadGb.ToString(CultureInfo.InvariantCulture)}",
            MamBonusSpend.Wedge => "spendtype=wedges&source=points",
            _ => "spendtype=VIP&duration=max",
        };
        var url = BuildUrl($"/json/bonusBuy.php/?{query}&_={_timeProvider.GetUtcNow().ToUnixTimeMilliseconds()}");
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken, retryTransient: false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseBonusPurchase(body);
    }

    internal static MamBonusPurchaseResult ParseBonusPurchase(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success)
                || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidOperationException($"MAM's answer to the purchase has no 'success': {Shorten(body)}");
            }

            return new MamBonusPurchaseResult(
                success.GetBoolean(),
                Optional(root, "seedbonus", e => (double?)MamJson.GetDouble(e)),
                Optional(root, "error", MamJson.GetString),
                Optional(root, "FLleft", e => (long?)MamJson.GetInt64(e)));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"MAM's answer to the purchase isn't JSON: {Shorten(body)}");
        }
    }

    private static string Shorten(string body) =>
        body.Length <= LoggedBodyLength ? body : body[..LoggedBodyLength] + "…";

    private async Task ApplyBonusPurchasePolitenessAsync(CancellationToken cancellationToken)
    {
        await _bonusPurchasePoliteness.WaitAsync(cancellationToken);
        try
        {
            if (_lastBonusPurchaseAt is { } last)
            {
                var remaining = BonusPurchasePoliteness - (_timeProvider.GetUtcNow() - last);
                if (remaining > TimeSpan.Zero)
                {
                    await _delay(remaining, cancellationToken);
                }
            }

            _lastBonusPurchaseAt = _timeProvider.GetUtcNow();
        }
        finally
        {
            _bonusPurchasePoliteness.Release();
        }
    }
}
