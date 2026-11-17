using System.Globalization;
using System.Text;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data;
using Fetchshelf.Data.Account;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Account;

/// <summary>One chart of the Account page's "Over time" section.</summary>
/// <param name="Title">What the chart shows, such as "Ratio".</param>
/// <param name="Svg">The chart itself (<see cref="SparklineSvg"/>).</param>
/// <param name="From">The first value in the period, formatted; null when there is none.</param>
/// <param name="To">The last value in the period, formatted; null when there is none.</param>
public sealed record AccountHistoryChart(string Title, string Svg, string? From, string? To);

/// <summary>One month of the Account page's month-by-month table: the month's last snapshot, and what changed since the
/// month before's last one. A change is null when the month before has no snapshot.</summary>
public sealed record AccountHistoryMonth(
    string Month, double Ratio, long UploadedBytes, long? UploadedChange, long DownloadedBytes, long? DownloadedChange,
    double? BonusPoints, double? BonusPointsChange, long Wedges);

/// <summary>The Account page's "Over time" section, from the daily snapshots <see cref="AccountSnapshotRecorder"/> keeps.</summary>
public sealed record AccountHistory(IReadOnlyList<AccountHistoryChart> Charts, IReadOnlyList<AccountHistoryMonth> Months)
{
    /// <summary>How many days the charts cover, today included.</summary>
    public const int ChartDays = 90;

    /// <summary>How many months the table shows.</summary>
    public const int TableMonths = 12;

    public bool IsEmpty => Charts.Count == 0;

    public static async Task<AccountHistory> LoadAsync(FetchshelfDbContext dbContext, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        // The months the table shows, plus the month before the first, which its changes are measured from.
        var since = new DateOnly(today.Year, today.Month, 1).AddMonths(-TableMonths).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rows = await dbContext.AccountSnapshots.AsNoTracking()
            .Where(s => string.Compare(s.Day, since) >= 0)
            .OrderBy(s => s.Day)
            .ToListAsync(cancellationToken);
        return Build(rows, today);
    }

    /// <summary>The section from <paramref name="rows"/>, oldest first, as of <paramref name="today"/> (UTC).</summary>
    public static AccountHistory Build(IReadOnlyList<AccountSnapshotEntity> rows, DateOnly today)
    {
        if (rows.Count == 0)
        {
            return new AccountHistory([], []);
        }

        var byDay = rows.ToDictionary(r => r.Day, StringComparer.Ordinal);
        var days = Enumerable.Range(0, ChartDays)
            .Select(offset => byDay.GetValueOrDefault(today.AddDays(offset - ChartDays + 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();
        var charts = new List<AccountHistoryChart>
        {
            Chart("Ratio", days, r => r.Ratio, v => v.ToString("0.##", CultureInfo.InvariantCulture)),
            Chart("Uploaded", days, r => r.UploadedBytes, DownloadBudget.FormatBytes),
            Chart("Bonus points", days, r => r.BonusPoints, v => v.ToString("N0", CultureInfo.InvariantCulture)),
            Chart("Wedges", days, r => r.Wedges, v => v.ToString("N0", CultureInfo.InvariantCulture)),
            Chart("Unsatisfied", days, r => r.UnsatCount, v => v.ToString("N0", CultureInfo.InvariantCulture)),
        };

        var monthEnds = rows.GroupBy(r => r.Day[..7]).Select(g => g.MaxBy(r => r.Day, StringComparer.Ordinal)!).OrderBy(r => r.Day).ToList();
        var months = new List<AccountHistoryMonth>();
        for (var i = 0; i < monthEnds.Count; i++)
        {
            var end = monthEnds[i];
            var before = i > 0 && monthEnds[i - 1].Day[..7] == PreviousMonth(end.Day[..7]) ? monthEnds[i - 1] : null;
            months.Add(new AccountHistoryMonth(
                end.Day[..7], end.Ratio,
                end.UploadedBytes, end.UploadedBytes - before?.UploadedBytes,
                end.DownloadedBytes, end.DownloadedBytes - before?.DownloadedBytes,
                end.BonusPoints, end.BonusPoints - before?.BonusPoints,
                end.Wedges));
        }

        return new AccountHistory(charts, months.AsEnumerable().Reverse().Take(TableMonths).ToList());
    }

    /// <summary>Every snapshot as CSV, oldest first, one row a day.</summary>
    public static string Csv(IEnumerable<AccountSnapshotEntity> rows)
    {
        var csv = new StringBuilder("day,taken_at,ratio,uploaded_bytes,downloaded_bytes,wedges,unsat_count,unsat_limit,bonus_points,class\n");
        foreach (var r in rows)
        {
            csv.Append(CultureInfo.InvariantCulture,
                $"{r.Day},{r.TakenAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ},{r.Ratio},{r.UploadedBytes},{r.DownloadedBytes},{r.Wedges},{r.UnsatCount},{r.UnsatLimit},{r.BonusPoints},{CsvField(r.ClassName)}\n");
        }

        return csv.ToString();
    }

    private static string CsvField(string? value) =>
        value is null ? string.Empty
        : value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    private static string PreviousMonth(string month) =>
        DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static AccountHistoryChart Chart(string title, IReadOnlyList<AccountSnapshotEntity?> days, Func<AccountSnapshotEntity, double?> value, Func<double, string> format)
    {
        var values = days.Select(d => d is null ? null : value(d)).ToList();
        var known = values.Where(v => v is { } x && double.IsFinite(x)).Select(v => v!.Value).ToList();
        var (from, to) = known.Count == 0 ? (null, null) : (format(known[0]), format(known[^1]));
        var description = known.Count == 0
            ? $"{title}: nothing recorded in the last {ChartDays} days"
            : $"{title} over the last {ChartDays} days, from {from} to {to}";
        return new AccountHistoryChart(title, SparklineSvg.Build(values, description), from, to);
    }
}
