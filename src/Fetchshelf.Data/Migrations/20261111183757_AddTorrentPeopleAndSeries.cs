using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddTorrentPeopleAndSeries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Language",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MediaType",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "TorrentPeople",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TorrentId = table.Column<string>(type: "TEXT", nullable: false),
                Role = table.Column<int>(type: "INTEGER", nullable: false),
                Position = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                NameKey = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TorrentPeople", x => x.Id);
                table.ForeignKey(
                    name: "FK_TorrentPeople_Torrents_TorrentId",
                    column: x => x.TorrentId,
                    principalTable: "Torrents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TorrentSeries",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TorrentId = table.Column<string>(type: "TEXT", nullable: false),
                Position = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                NameKey = table.Column<string>(type: "TEXT", nullable: false),
                Kind = table.Column<int>(type: "INTEGER", nullable: true),
                FirstNumber = table.Column<double>(type: "REAL", nullable: true),
                LastNumber = table.Column<double>(type: "REAL", nullable: true),
                Display = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TorrentSeries", x => x.Id);
                table.ForeignKey(
                    name: "FK_TorrentSeries_Torrents_TorrentId",
                    column: x => x.TorrentId,
                    principalTable: "Torrents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Torrents_Language",
            table: "Torrents",
            column: "Language");

        migrationBuilder.CreateIndex(
            name: "IX_Torrents_MediaType",
            table: "Torrents",
            column: "MediaType");

        migrationBuilder.CreateIndex(
            name: "IX_TorrentPeople_Role_NameKey",
            table: "TorrentPeople",
            columns: new[] { "Role", "NameKey" });

        migrationBuilder.CreateIndex(
            name: "IX_TorrentPeople_TorrentId",
            table: "TorrentPeople",
            column: "TorrentId");

        migrationBuilder.CreateIndex(
            name: "IX_TorrentSeries_NameKey",
            table: "TorrentSeries",
            column: "NameKey");

        migrationBuilder.CreateIndex(
            name: "IX_TorrentSeries_TorrentId",
            table: "TorrentSeries",
            column: "TorrentId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TorrentPeople");

        migrationBuilder.DropTable(
            name: "TorrentSeries");

        migrationBuilder.DropIndex(
            name: "IX_Torrents_Language",
            table: "Torrents");

        migrationBuilder.DropIndex(
            name: "IX_Torrents_MediaType",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "Language",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "MediaType",
            table: "Torrents");
    }
}
