using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class PlayerExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Experience",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Prestige",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Experience",
                table: "PlayerContextExtensions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "PlayerContextExtensions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Prestige",
                table: "PlayerContextExtensions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Experience",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Prestige",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Experience",
                table: "PlayerContextExtensions");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "PlayerContextExtensions");

            migrationBuilder.DropColumn(
                name: "Prestige",
                table: "PlayerContextExtensions");
        }
    }
}
