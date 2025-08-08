using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class MapBools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresChroma",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresCinema",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresGroupLighting",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresMappingExtensions",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresNoodles",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresOptionalProperties",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresV3",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresV3Pepega",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresVNJS",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresVivify",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TypeAcc",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TypeMidspeed",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TypeSpeed",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TypeTech",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresChroma",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "RequiresCinema",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "RequiresGroupLighting",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "RequiresMappingExtensions",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "RequiresNoodles",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "RequiresOptionalProperties",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "RequiresV3",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "RequiresV3Pepega",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "RequiresVNJS",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "RequiresVivify",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "TypeAcc",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "TypeMidspeed",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "TypeSpeed",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "TypeTech",
                table: "DifficultyDescription");
        }
    }
}
