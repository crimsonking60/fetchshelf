using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddAccountSnapshots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AccountSnapshots",
            columns: table => new
            {
                Day = table.Column<string>(type: "TEXT", nullable: false),
                TakenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Ratio = table.Column<double>(type: "REAL", nullable: false),
                UploadedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                DownloadedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                Wedges = table.Column<long>(type: "INTEGER", nullable: false),
                UnsatCount = table.Column<long>(type: "INTEGER", nullable: false),
                UnsatLimit = table.Column<long>(type: "INTEGER", nullable: false),
                BonusPoints = table.Column<double>(type: "REAL", nullable: true),
                ClassName = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountSnapshots", x => x.Day);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AccountSnapshots");
    }
}
