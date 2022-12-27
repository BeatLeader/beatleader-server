using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class TimingInScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AverageLeftTiming",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "AverageRightTiming",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "MaxStreak",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "LeftTiming",
                table: "Scores",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "MaxStreak",
                table: "Scores",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "RightTiming",
                table: "Scores",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "AverageLeftTiming",
                table: "PlayerScoreStatsHistory",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "AverageRightTiming",
                table: "PlayerScoreStatsHistory",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "MaxStreak",
                table: "PlayerScoreStatsHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AverageLeftTiming",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "AverageRightTiming",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "MaxStreak",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "LeftTiming",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "MaxStreak",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "RightTiming",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "AverageLeftTiming",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.DropColumn(
                name: "AverageRightTiming",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.DropColumn(
                name: "MaxStreak",
                table: "PlayerScoreStatsHistory");
        }
    }
}
