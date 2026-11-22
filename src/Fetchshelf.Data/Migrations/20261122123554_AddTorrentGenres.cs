using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddTorrentGenres : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FictionCategory",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "TorrentGenres",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TorrentId = table.Column<string>(type: "TEXT", nullable: false),
                Genre = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TorrentGenres", x => x.Id);
                table.ForeignKey(
                    name: "FK_TorrentGenres_Torrents_TorrentId",
                    column: x => x.TorrentId,
                    principalTable: "Torrents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Torrents_FictionCategory",
            table: "Torrents",
            column: "FictionCategory");

        migrationBuilder.CreateIndex(
            name: "IX_TorrentGenres_Genre",
            table: "TorrentGenres",
            column: "Genre");

        migrationBuilder.CreateIndex(
            name: "IX_TorrentGenres_TorrentId",
            table: "TorrentGenres",
            column: "TorrentId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TorrentGenres");

        migrationBuilder.DropIndex(
            name: "IX_Torrents_FictionCategory",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "FictionCategory",
            table: "Torrents");
    }
}
