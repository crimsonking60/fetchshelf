using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class RemoveUnusedStorage : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ConfigStore");

        migrationBuilder.DropColumn(
            name: "ListItemReference",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "InfoHash",
            table: "SelectedTorrents");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ListItemReference",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "InfoHash",
            table: "SelectedTorrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ConfigStore",
            columns: table => new
            {
                Key = table.Column<string>(type: "TEXT", nullable: false),
                Value = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConfigStore", x => x.Key);
            });
    }
}
