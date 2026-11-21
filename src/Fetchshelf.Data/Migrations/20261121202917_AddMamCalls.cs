using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddMamCalls : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MamCalls",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                StartedAtMs = table.Column<long>(type: "INTEGER", nullable: false),
                DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                Method = table.Column<string>(type: "TEXT", nullable: false),
                Host = table.Column<string>(type: "TEXT", nullable: false),
                Path = table.Column<string>(type: "TEXT", nullable: false),
                StatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                Failure = table.Column<string>(type: "TEXT", nullable: true),
                Caller = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MamCalls", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MamCalls_StartedAtMs",
            table: "MamCalls",
            column: "StartedAtMs");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MamCalls");
    }
}
