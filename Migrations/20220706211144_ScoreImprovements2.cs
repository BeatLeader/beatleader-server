using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class ScoreImprovements2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BadCuts",
                table: "ScoreImprovement",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BombCuts",
                table: "ScoreImprovement",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MissedNotes",
                table: "ScoreImprovement",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Pauses",
                table: "ScoreImprovement",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Timeset",
                table: "ScoreImprovement",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "WallsHit",
                table: "ScoreImprovement",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BadCuts",
                table: "ScoreImprovement");

            migrationBuilder.DropColumn(
                name: "BombCuts",
                table: "ScoreImprovement");

            migrationBuilder.DropColumn(
                name: "MissedNotes",
                table: "ScoreImprovement");

            migrationBuilder.DropColumn(
                name: "Pauses",
                table: "ScoreImprovement");

            migrationBuilder.DropColumn(
                name: "Timeset",
                table: "ScoreImprovement");

            migrationBuilder.DropColumn(
                name: "WallsHit",
                table: "ScoreImprovement");
        }
    }
}
