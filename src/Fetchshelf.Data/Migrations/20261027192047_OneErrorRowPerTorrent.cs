using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class OneErrorRowPerTorrent : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ErroredTorrents_Stage_Key",
            table: "ErroredTorrents");

        migrationBuilder.AddColumn<string>(
            name: "Title",
            table: "ErroredTorrents",
            type: "TEXT",
            nullable: true);

        // Repeat failures used to add a row each time; keep only the newest per (stage, key).
        migrationBuilder.Sql(
            "DELETE FROM ErroredTorrents WHERE Id NOT IN (SELECT MAX(Id) FROM ErroredTorrents GROUP BY Stage, Key);");

        migrationBuilder.CreateIndex(
            name: "IX_ErroredTorrents_Stage_Key",
            table: "ErroredTorrents",
            columns: new[] { "Stage", "Key" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ErroredTorrents_Stage_Key",
            table: "ErroredTorrents");

        migrationBuilder.DropColumn(
            name: "Title",
            table: "ErroredTorrents");

        migrationBuilder.CreateIndex(
            name: "IX_ErroredTorrents_Stage_Key",
            table: "ErroredTorrents",
            columns: new[] { "Stage", "Key" });
    }
}
