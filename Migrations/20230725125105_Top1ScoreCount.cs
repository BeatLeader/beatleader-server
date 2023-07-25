using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class Top1ScoreCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RankedTop1Count",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RankedTop1Score",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Top1Count",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Top1Score",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnrankedTop1Count",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnrankedTop1Score",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RankedTop1Count",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "RankedTop1Score",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "Top1Count",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "Top1Score",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "UnrankedTop1Count",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "UnrankedTop1Score",
                table: "Stats");
        }
    }
}
