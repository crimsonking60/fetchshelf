using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddJobRuns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "JobRuns",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                JobName = table.Column<string>(type: "TEXT", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                Result = table.Column<int>(type: "INTEGER", nullable: false),
                Trigger = table.Column<int>(type: "INTEGER", nullable: false),
                Summary = table.Column<string>(type: "TEXT", nullable: true),
                CountsJson = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobRuns", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_JobRuns_JobName",
            table: "JobRuns",
            column: "JobName");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "JobRuns");
    }
}
