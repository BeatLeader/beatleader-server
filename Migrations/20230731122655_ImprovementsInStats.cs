using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ImprovementsInStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RankedImprovementsCount",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalImprovementsCount",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnrankedImprovementsCount",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RankedImprovementsCount",
                table: "PlayerScoreStatsHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalImprovementsCount",
                table: "PlayerScoreStatsHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnrankedImprovementsCount",
                table: "PlayerScoreStatsHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RankedImprovementsCount",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "TotalImprovementsCount",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "UnrankedImprovementsCount",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "RankedImprovementsCount",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.DropColumn(
                name: "TotalImprovementsCount",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.DropColumn(
                name: "UnrankedImprovementsCount",
                table: "PlayerScoreStatsHistory");
        }
    }
}
