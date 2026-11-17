using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class AddSeedingState : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "OnCleanedHeld",
            table: "Torrents",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "SeedingCheckedAt",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "SeedingSeconds",
            table: "Torrents",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "SeedingSince",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SeedingState",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Torrents_SeedingState",
            table: "Torrents",
            column: "SeedingState");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Torrents_SeedingState",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "OnCleanedHeld",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "SeedingCheckedAt",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "SeedingSeconds",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "SeedingSince",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "SeedingState",
            table: "Torrents");
    }
}
