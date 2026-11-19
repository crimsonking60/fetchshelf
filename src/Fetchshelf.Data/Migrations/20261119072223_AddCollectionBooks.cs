using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddCollectionBooks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CollectionBookCount",
            table: "Torrents",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CollectionTorrentId",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "SplitCollection",
            table: "Torrents",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Torrents_CollectionTorrentId",
            table: "Torrents",
            column: "CollectionTorrentId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Torrents_CollectionTorrentId",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "CollectionBookCount",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "CollectionTorrentId",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "SplitCollection",
            table: "Torrents");
    }
}
