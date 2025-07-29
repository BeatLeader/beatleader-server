using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.Storage
{
    /// <inheritdoc />
    public partial class Playtimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ScorePlaytime",
                table: "PlayerScoreStatsHistory",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "SteamPlaytime2Weeks",
                table: "PlayerScoreStatsHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SteamPlaytimeForever",
                table: "PlayerScoreStatsHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScorePlaytime",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.DropColumn(
                name: "SteamPlaytime2Weeks",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.DropColumn(
                name: "SteamPlaytimeForever",
                table: "PlayerScoreStatsHistory");
        }
    }
}
