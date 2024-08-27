using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ClanHistoryExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AverageAccuracy",
                table: "GlobalMapHistory",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "AverageRank",
                table: "GlobalMapHistory",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "CaptureLeaderboardsCount",
                table: "GlobalMapHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlayersCount",
                table: "GlobalMapHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "Pp",
                table: "GlobalMapHistory",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "Rank",
                table: "GlobalMapHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AverageAccuracy",
                table: "GlobalMapHistory");

            migrationBuilder.DropColumn(
                name: "AverageRank",
                table: "GlobalMapHistory");

            migrationBuilder.DropColumn(
                name: "CaptureLeaderboardsCount",
                table: "GlobalMapHistory");

            migrationBuilder.DropColumn(
                name: "PlayersCount",
                table: "GlobalMapHistory");

            migrationBuilder.DropColumn(
                name: "Pp",
                table: "GlobalMapHistory");

            migrationBuilder.DropColumn(
                name: "Rank",
                table: "GlobalMapHistory");
        }
    }
}
