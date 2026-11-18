using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddFindLanguages : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Language",
            table: "FollowFinds",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Language",
            table: "AudiobookshelfSeriesBooks",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Language",
            table: "FollowFinds");

        migrationBuilder.DropColumn(
            name: "Language",
            table: "AudiobookshelfSeriesBooks");
    }
}
