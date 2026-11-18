using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddDownloadClientAndSavePath : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ClientName",
            table: "SelectedTorrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SavePath",
            table: "SelectedTorrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ClientName",
            table: "Events",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ClientName",
            table: "SelectedTorrents");

        migrationBuilder.DropColumn(
            name: "SavePath",
            table: "SelectedTorrents");

        migrationBuilder.DropColumn(
            name: "ClientName",
            table: "Events");
    }
}
