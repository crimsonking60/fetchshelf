using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddBonusPurchaseEvents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BonusSpend",
            table: "Events",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "BonusUploadGb",
            table: "Events",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "PointsAfter",
            table: "Events",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "PointsBefore",
            table: "Events",
            type: "REAL",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BonusSpend",
            table: "Events");

        migrationBuilder.DropColumn(
            name: "BonusUploadGb",
            table: "Events");

        migrationBuilder.DropColumn(
            name: "PointsAfter",
            table: "Events");

        migrationBuilder.DropColumn(
            name: "PointsBefore",
            table: "Events");
    }
}
