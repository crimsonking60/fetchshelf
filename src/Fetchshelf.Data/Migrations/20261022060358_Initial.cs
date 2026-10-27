using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fetchshelf.Data.Migrations;

/// <inheritdoc />
public partial class Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ConfigStore",
            columns: table => new
            {
                Key = table.Column<string>(type: "TEXT", nullable: false),
                Value = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConfigStore", x => x.Key);
            });

        migrationBuilder.CreateTable(
            name: "DuplicateTorrents",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                MamId = table.Column<long>(type: "INTEGER", nullable: false),
                MetadataJson = table.Column<string>(type: "TEXT", nullable: false),
                DuplicateOfTorrentId = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DuplicateTorrents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ErroredTorrents",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Stage = table.Column<int>(type: "INTEGER", nullable: false),
                Key = table.Column<string>(type: "TEXT", nullable: false),
                ErrorText = table.Column<string>(type: "TEXT", nullable: false),
                DisplayMetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ErroredTorrents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Events",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                TorrentId = table.Column<string>(type: "TEXT", nullable: true),
                MamId = table.Column<long>(type: "INTEGER", nullable: true),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                GrabberName = table.Column<string>(type: "TEXT", nullable: true),
                Cost = table.Column<string>(type: "TEXT", nullable: true),
                WedgeUsed = table.Column<bool>(type: "INTEGER", nullable: true),
                LinkerName = table.Column<string>(type: "TEXT", nullable: true),
                LibraryPath = table.Column<string>(type: "TEXT", nullable: true),
                FilesRemovedJson = table.Column<string>(type: "TEXT", nullable: true),
                FieldChangesJson = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ListItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ListId = table.Column<string>(type: "TEXT", nullable: false),
                BookTitle = table.Column<string>(type: "TEXT", nullable: false),
                AudioStatus = table.Column<string>(type: "TEXT", nullable: true),
                EbookStatus = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListItems", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Lists",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                ListType = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Lists", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "SelectedTorrents",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                MamId = table.Column<long>(type: "INTEGER", nullable: false),
                DownloadLink = table.Column<string>(type: "TEXT", nullable: false),
                InfoHash = table.Column<string>(type: "TEXT", nullable: true),
                CostDecision = table.Column<string>(type: "TEXT", nullable: false),
                Category = table.Column<string>(type: "TEXT", nullable: true),
                TagsJson = table.Column<string>(type: "TEXT", nullable: true),
                UnsatBuffer = table.Column<int>(type: "INTEGER", nullable: false),
                WedgeBuffer = table.Column<int>(type: "INTEGER", nullable: false),
                GrabberName = table.Column<string>(type: "TEXT", nullable: true),
                MetadataJson = table.Column<string>(type: "TEXT", nullable: false),
                NormalizedTitleKey = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                RemovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SelectedTorrents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Torrents",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                MamId = table.Column<long>(type: "INTEGER", nullable: false),
                NormalizedTitleKey = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                MetadataJson = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                LibraryPath = table.Column<string>(type: "TEXT", nullable: true),
                LinkedFilesJson = table.Column<string>(type: "TEXT", nullable: true),
                ChosenAudioExtension = table.Column<string>(type: "TEXT", nullable: true),
                ChosenEbookExtension = table.Column<string>(type: "TEXT", nullable: true),
                QBittorrentCategory = table.Column<string>(type: "TEXT", nullable: true),
                LinkerName = table.Column<string>(type: "TEXT", nullable: true),
                AbsId = table.Column<string>(type: "TEXT", nullable: true),
                ReplacedWithTorrentId = table.Column<string>(type: "TEXT", nullable: true),
                ReplacedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LibraryMismatchState = table.Column<string>(type: "TEXT", nullable: true),
                ClientStatus = table.Column<string>(type: "TEXT", nullable: true),
                ListItemReference = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Torrents", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ErroredTorrents_Stage_Key",
            table: "ErroredTorrents",
            columns: new[] { "Stage", "Key" });

        migrationBuilder.CreateIndex(
            name: "IX_Events_MamId",
            table: "Events",
            column: "MamId");

        migrationBuilder.CreateIndex(
            name: "IX_Events_TorrentId",
            table: "Events",
            column: "TorrentId");

        migrationBuilder.CreateIndex(
            name: "IX_ListItems_ListId",
            table: "ListItems",
            column: "ListId");

        migrationBuilder.CreateIndex(
            name: "IX_SelectedTorrents_MamId",
            table: "SelectedTorrents",
            column: "MamId");

        migrationBuilder.CreateIndex(
            name: "IX_Torrents_MamId",
            table: "Torrents",
            column: "MamId");

        migrationBuilder.CreateIndex(
            name: "IX_Torrents_NormalizedTitleKey",
            table: "Torrents",
            column: "NormalizedTitleKey");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ConfigStore");

        migrationBuilder.DropTable(
            name: "DuplicateTorrents");

        migrationBuilder.DropTable(
            name: "ErroredTorrents");

        migrationBuilder.DropTable(
            name: "Events");

        migrationBuilder.DropTable(
            name: "ListItems");

        migrationBuilder.DropTable(
            name: "Lists");

        migrationBuilder.DropTable(
            name: "SelectedTorrents");

        migrationBuilder.DropTable(
            name: "Torrents");
    }
}
