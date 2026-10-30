using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class RemoveGoodreadsLists : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ListItems");

        migrationBuilder.DropTable(
            name: "Lists");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ListItems",
            columns: table => new
            {
                ListId = table.Column<string>(type: "TEXT", nullable: false),
                Guid = table.Column<string>(type: "TEXT", nullable: false),
                AllowAudio = table.Column<bool>(type: "INTEGER", nullable: false),
                AllowEbook = table.Column<bool>(type: "INTEGER", nullable: false),
                AudioAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                AudioMamId = table.Column<long>(type: "INTEGER", nullable: true),
                AudioStatus = table.Column<string>(type: "TEXT", nullable: true),
                AuthorsJson = table.Column<string>(type: "TEXT", nullable: false),
                BookTitle = table.Column<string>(type: "TEXT", nullable: false),
                BookUrl = table.Column<string>(type: "TEXT", nullable: true),
                CoverUrl = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                EbookAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                EbookMamId = table.Column<long>(type: "INTEGER", nullable: true),
                EbookStatus = table.Column<string>(type: "TEXT", nullable: true),
                Isbn = table.Column<string>(type: "TEXT", nullable: true),
                MarkedDoneAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                PreferFormat = table.Column<string>(type: "TEXT", nullable: true),
                SeriesName = table.Column<string>(type: "TEXT", nullable: true),
                SeriesNumber = table.Column<double>(type: "REAL", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListItems", x => new { x.ListId, x.Guid });
            });

        migrationBuilder.CreateTable(
            name: "Lists",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                BuildDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ListType = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Lists", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ListItems_ListId",
            table: "ListItems",
            column: "ListId");
    }
}
