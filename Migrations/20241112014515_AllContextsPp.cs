using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class AllContextsPp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AllContextsPp",
                table: "Players",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "MainPlayersCount",
                table: "GlobalMapHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MainPlayersCount",
                table: "Clans",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllContextsPp",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "MainPlayersCount",
                table: "GlobalMapHistory");

            migrationBuilder.DropColumn(
                name: "MainPlayersCount",
                table: "Clans");
        }
    }
}
