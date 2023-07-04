using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    /// <inheritdoc />
    public partial class MaxStreakInStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimesetMig",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "TimesetMig",
                table: "ScoreImprovement");

            migrationBuilder.DropColumn(
                name: "TimesetMig",
                table: "FailedScores");

            migrationBuilder.AddColumn<int>(
                name: "RankedMaxStreak",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnrankedMaxStreak",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RankedMaxStreak",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "UnrankedMaxStreak",
                table: "Stats");

            migrationBuilder.AddColumn<int>(
                name: "TimesetMig",
                table: "Scores",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TimesetMig",
                table: "ScoreImprovement",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TimesetMig",
                table: "FailedScores",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
