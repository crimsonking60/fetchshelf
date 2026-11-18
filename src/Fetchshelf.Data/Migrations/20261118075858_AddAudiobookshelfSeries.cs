using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddAudiobookshelfSeries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Source",
            table: "Follows",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "AudiobookshelfSeriesBooks",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ItemId = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                NameKey = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                Display = table.Column<string>(type: "TEXT", nullable: true),
                FirstNumber = table.Column<double>(type: "REAL", nullable: true),
                LastNumber = table.Column<double>(type: "REAL", nullable: true),
                MediaType = table.Column<string>(type: "TEXT", nullable: false),
                SyncedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AudiobookshelfSeriesBooks", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AudiobookshelfSeriesBooks_NameKey",
            table: "AudiobookshelfSeriesBooks",
            column: "NameKey");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AudiobookshelfSeriesBooks");

        migrationBuilder.DropColumn(
            name: "Source",
            table: "Follows");
    }
}
