using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>
/// Questions the Link job asks once per torrent must find their rows through an index: a question that reads the whole
/// Torrents table, asked for each of thousands of torrents, made a run with nothing to do take a minute.
/// </summary>
public sealed class TorrentIndexTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-index-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public void AnotherEditionInTheSameFolder_IsFoundThroughTheLibraryPathIndex()
    {
        using var context = OrganizerTestSupport.CreateContext(_dbPath);
        const string plain = "/library/Author/Title";
        const string torrentId = "hash1";
        const string language = "English";

        // The Link job's question in BookFolderAsync, as EF turns it into SQL.
        var sql = context.Torrents
            .Where(t => t.LibraryPath == plain && t.Id != torrentId && t.Language != null && t.Language != language)
            .ToQueryString();

        var plan = QueryPlan(context, sql);
        Assert.Contains("USING INDEX IX_Torrents_LibraryPath", plan);
        Assert.DoesNotContain("SCAN", plan);
    }

    private static string QueryPlan(FetchshelfDbContext context, string sql)
    {
        // ToQueryString declares the parameters in comments first; SQLite reads an unset one as null, which doesn't
        // change the plan.
        var statement = string.Join('\n', sql.Split('\n').Where(line => !line.StartsWith("--", StringComparison.Ordinal)));
        var connection = context.Database.GetDbConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + statement;
        using var reader = command.ExecuteReader();
        var lines = new List<string>();
        while (reader.Read())
        {
            lines.Add(reader.GetString(3));
        }

        return string.Join('\n', lines);
    }
}
