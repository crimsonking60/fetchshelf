using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddFollows : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Follows",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                NameKey = table.Column<string>(type: "TEXT", nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                Mode = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                LastSearchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LastFoundAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Follows", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "FollowFinds",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                FollowId = table.Column<int>(type: "INTEGER", nullable: false),
                MamId = table.Column<long>(type: "INTEGER", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                SeriesDisplay = table.Column<string>(type: "TEXT", nullable: true),
                MediaSide = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                FoundAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                StatusAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FollowFinds", x => x.Id);
                table.ForeignKey(
                    name: "FK_FollowFinds_Follows_FollowId",
                    column: x => x.FollowId,
                    principalTable: "Follows",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FollowFinds_FollowId_MamId",
            table: "FollowFinds",
            columns: new[] { "FollowId", "MamId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_FollowFinds_Status",
            table: "FollowFinds",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_Follows_Kind_NameKey",
            table: "Follows",
            columns: new[] { "Kind", "NameKey" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "FollowFinds");

        migrationBuilder.DropTable(
            name: "Follows");
    }
}
