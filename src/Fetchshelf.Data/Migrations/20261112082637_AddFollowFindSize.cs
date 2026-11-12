using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddFollowFindSize : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "SizeBytes",
            table: "FollowFinds",
            type: "INTEGER",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SizeBytes",
            table: "FollowFinds");
    }
}
