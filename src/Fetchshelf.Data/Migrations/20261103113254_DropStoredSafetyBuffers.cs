using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class DropStoredSafetyBuffers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "UnsatBuffer",
            table: "SelectedTorrents");

        migrationBuilder.DropColumn(
            name: "WedgeBuffer",
            table: "SelectedTorrents");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "UnsatBuffer",
            table: "SelectedTorrents",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "WedgeBuffer",
            table: "SelectedTorrents",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);
    }
}
