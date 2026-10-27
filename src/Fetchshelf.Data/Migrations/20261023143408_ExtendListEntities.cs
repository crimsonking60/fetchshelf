using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class ExtendListEntities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "BuildDate",
            table: "Lists",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedAt",
            table: "Lists",
            type: "TEXT",
            nullable: true);

        // ListItems was never populated (step 1 scaffolding, unused until this change) - drop and
        // recreate with the full shape rather than a long column-by-column migration.
        migrationBuilder.DropTable(
            name: "ListItems");

        migrationBuilder.CreateTable(
            name: "ListItems",
            columns: table => new
            {
                ListId = table.Column<string>(type: "TEXT", nullable: false),
                Guid = table.Column<string>(type: "TEXT", nullable: false),
                BookTitle = table.Column<string>(type: "TEXT", nullable: false),
                AuthorsJson = table.Column<string>(type: "TEXT", nullable: false),
                SeriesName = table.Column<string>(type: "TEXT", nullable: true),
                SeriesNumber = table.Column<double>(type: "REAL", nullable: true),
                CoverUrl = table.Column<string>(type: "TEXT", nullable: false),
                BookUrl = table.Column<string>(type: "TEXT", nullable: true),
                Isbn = table.Column<string>(type: "TEXT", nullable: true),
                PreferFormat = table.Column<string>(type: "TEXT", nullable: true),
                AllowAudio = table.Column<bool>(type: "INTEGER", nullable: false),
                AllowEbook = table.Column<bool>(type: "INTEGER", nullable: false),
                AudioMamId = table.Column<long>(type: "INTEGER", nullable: true),
                AudioStatus = table.Column<string>(type: "TEXT", nullable: true),
                AudioAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                EbookMamId = table.Column<long>(type: "INTEGER", nullable: true),
                EbookStatus = table.Column<string>(type: "TEXT", nullable: true),
                EbookAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                MarkedDoneAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListItems", x => new { x.ListId, x.Guid });
            });

        migrationBuilder.CreateIndex(
            name: "IX_ListItems_ListId",
            table: "ListItems",
            column: "ListId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BuildDate",
            table: "Lists");

        migrationBuilder.DropColumn(
            name: "UpdatedAt",
            table: "Lists");

        migrationBuilder.DropTable(
            name: "ListItems");

        migrationBuilder.CreateTable(
            name: "ListItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ListId = table.Column<string>(type: "TEXT", nullable: false),
                BookTitle = table.Column<string>(type: "TEXT", nullable: false),
                AudioStatus = table.Column<string>(type: "TEXT", nullable: true),
                EbookStatus = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListItems", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ListItems_ListId",
            table: "ListItems",
            column: "ListId");
    }
}
