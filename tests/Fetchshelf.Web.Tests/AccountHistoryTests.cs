using Fetchshelf.Data.Entities;
using Fetchshelf.Web.Pages.Account;

namespace Fetchshelf.Web.Tests;

public sealed class AccountHistoryTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    private static AccountSnapshotEntity Row(string day, double ratio = 2, long uploaded = 1000, long downloaded = 500, double? points = 100, long wedges = 5) => new()
    {
        Day = day,
        TakenAt = DateTimeOffset.Parse(day + "T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        Ratio = ratio,
        UploadedBytes = uploaded,
        DownloadedBytes = downloaded,
        BonusPoints = points,
        Wedges = wedges,
    };

    [Fact]
    public void Build_NoRows_IsEmpty() => Assert.True(AccountHistory.Build([], Today).IsEmpty);

    [Fact]
    public void Build_Charts_ShowTheLast90Days_FromTheFirstValueToTheLast()
    {
        var history = AccountHistory.Build([Row("2026-05-01", ratio: 9), Row("2026-10-01", ratio: 1.5), Row("2026-11-17", ratio: 2.25)], Today);

        Assert.Equal(["Ratio", "Uploaded", "Bonus points", "Wedges"], history.Charts.Select(c => c.Title));
        var ratio = history.Charts[0];
        Assert.Equal(("1.5", "2.25"), (ratio.From, ratio.To));
        Assert.Contains("Ratio over the last 90 days, from 1.5 to 2.25", ratio.Svg);
    }

    [Fact]
    public void Build_Months_AreEachMonthsLastDay_NewestFirst_WithTheChangeSinceTheMonthBefore()
    {
        var history = AccountHistory.Build(
            [
                Row("2026-08-31", uploaded: 100, downloaded: 50, points: 1000),
                Row("2026-09-10", uploaded: 150, downloaded: 60, points: 900),
                Row("2026-09-30", uploaded: 200, downloaded: 70, points: 1500),
                Row("2026-11-17", uploaded: 300, downloaded: 90, points: 1200),
            ],
            Today);

        Assert.Equal(["2026-11", "2026-09", "2026-08"], history.Months.Select(m => m.Month));
        var september = history.Months[1];
        Assert.Equal((200L, 100L, 70L, 20L, 1500.0, 500.0), (september.UploadedBytes, september.UploadedChange, september.DownloadedBytes, september.DownloadedChange, september.BonusPoints, september.BonusPointsChange));
        // October has no snapshot, so November's change has nothing to be measured from.
        Assert.Null(history.Months[0].UploadedChange);
        Assert.Null(history.Months[2].UploadedChange);
    }

    [Fact]
    public void Build_Months_ShowsAtMostTwelve()
    {
        var rows = Enumerable.Range(0, 15).Select(i => Row(Today.AddMonths(-i).ToString("yyyy-MM-01", System.Globalization.CultureInfo.InvariantCulture))).Reverse().ToList();

        var history = AccountHistory.Build(rows, Today);

        Assert.Equal(12, history.Months.Count);
        Assert.Equal("2026-11", history.Months[0].Month);
    }

    [Fact]
    public void Csv_HasAHeaderAndOneLineADay_WithTheClassQuotedWhenItNeedsIt()
    {
        var row = Row("2026-11-17", ratio: 2.5);
        row.ClassName = "Power User, \"VIP\"";
        row.UnsatCount = 12;
        row.UnsatLimit = 200;

        var lines = AccountHistory.Csv([row, Row("2026-11-18", points: null)]).TrimEnd('\n').Split('\n');

        Assert.Equal("day,taken_at,ratio,uploaded_bytes,downloaded_bytes,wedges,unsat_count,unsat_limit,bonus_points,class", lines[0]);
        Assert.Equal("2026-11-17,2026-11-17T12:00:00Z,2.5,1000,500,5,12,200,100,\"Power User, \"\"VIP\"\"\"", lines[1]);
        Assert.Equal("2026-11-18,2026-11-18T12:00:00Z,2,1000,500,5,0,0,,", lines[2]);
    }
}
