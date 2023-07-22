using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class Cleanups : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllTime",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LastTwoWeeksTime",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Nominated",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "Qualified",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "Ranked",
                table: "DifficultyDescription");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AllTime",
                table: "Players",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "LastTwoWeeksTime",
                table: "Players",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<bool>(
                name: "Nominated",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Qualified",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Ranked",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
