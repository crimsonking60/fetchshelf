using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fetchshelf.Data;

/// <summary>
/// Sets SQLite's own busy timeout on every connection Fetchshelf opens. Microsoft.Data.Sqlite waits for a locked
/// database in its own retry loop, but not when a write that returns values (EF Core's inserts and updates do) commits
/// while another connection is reading: SQLite then answers "database is locked" straight away. With the busy timeout,
/// SQLite waits for the reader itself, as it does for every other lock.
/// </summary>
public sealed class SqliteBusyTimeout : DbConnectionInterceptor
{
    /// <summary>How long a connection waits for another one's lock before it gives up.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static readonly string Pragma = $"PRAGMA busy_timeout = {(int)Timeout.TotalMilliseconds};";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragma;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragma;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public static class FetchshelfSqlite
{
    /// <summary>Points the context at the SQLite file, with the <see cref="SqliteBusyTimeout"/> every connection needs.</summary>
    public static DbContextOptionsBuilder UseFetchshelfSqlite(this DbContextOptionsBuilder options, string dataFilePath) =>
        options.UseSqlite($"Data Source={dataFilePath}").AddInterceptors(new SqliteBusyTimeout());

    /// <inheritdoc cref="UseFetchshelfSqlite(DbContextOptionsBuilder, string)"/>
    public static DbContextOptionsBuilder<TContext> UseFetchshelfSqlite<TContext>(this DbContextOptionsBuilder<TContext> options, string dataFilePath)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseFetchshelfSqlite((DbContextOptionsBuilder)options, dataFilePath);
}
