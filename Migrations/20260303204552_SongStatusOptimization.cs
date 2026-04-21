using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class SongStatusOptimization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBeastSaberAwarded",
                table: "Songs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsBuildingBlocksAwarded",
                table: "Songs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurated",
                table: "Songs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFeaturedOnCC",
                table: "Songs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMapOfTheWeek",
                table: "Songs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsNoodleMonday",
                table: "Songs",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBeastSaberAwarded",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "IsBuildingBlocksAwarded",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "IsCurated",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "IsFeaturedOnCC",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "IsMapOfTheWeek",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "IsNoodleMonday",
                table: "Songs");
        }
    }
}
