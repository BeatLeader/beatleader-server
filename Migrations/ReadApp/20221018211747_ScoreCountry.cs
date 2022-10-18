using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class ScoreCountry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplayWatched",
                table: "Scores");

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Scores",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Country",
                table: "Scores");

            migrationBuilder.AddColumn<int>(
                name: "ReplayWatched",
                table: "Scores",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
