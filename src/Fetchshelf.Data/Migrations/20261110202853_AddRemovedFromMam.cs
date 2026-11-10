using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddRemovedFromMam : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "MamCheckedAt",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "RemovedFromMamAt",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        // The client status was meant to hold this mark, but qBittorrent updates overwrite it; it gets its own column.
        migrationBuilder.Sql(
            """
            UPDATE Torrents
            SET RemovedFromMamAt = strftime('%Y-%m-%d %H:%M:%S', 'now') || '+00:00', ClientStatus = NULL
            WHERE ClientStatus = 'removed from MAM';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "MamCheckedAt",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "RemovedFromMamAt",
            table: "Torrents");
    }
}
