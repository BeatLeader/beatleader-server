using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class WatchingStatsFixes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IPHash",
                table: "WatchingSessions");

            migrationBuilder.RenameColumn(
                name: "ReplaysWatched",
                table: "Stats",
                newName: "AuthorizedReplayWatched");

            migrationBuilder.AddColumn<string>(
                name: "IP",
                table: "WatchingSessions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Player",
                table: "WatchingSessions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnonimusReplayWatched",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AnonimusReplayWatched",
                table: "Scores",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AuthorizedReplayWatched",
                table: "Scores",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IP",
                table: "WatchingSessions");

            migrationBuilder.DropColumn(
                name: "Player",
                table: "WatchingSessions");

            migrationBuilder.DropColumn(
                name: "AnonimusReplayWatched",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "AnonimusReplayWatched",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "AuthorizedReplayWatched",
                table: "Scores");

            migrationBuilder.RenameColumn(
                name: "AuthorizedReplayWatched",
                table: "Stats",
                newName: "ReplaysWatched");

            migrationBuilder.AddColumn<int>(
                name: "IPHash",
                table: "WatchingSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
