using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class AverageWeightedRankedRank : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AverageWeightedRankedRank",
                table: "StatsHistory",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<float>(
                name: "AverageWeightedRankedRank",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AverageWeightedRankedRank",
                table: "StatsHistory");

            migrationBuilder.DropColumn(
                name: "AverageWeightedRankedRank",
                table: "Stats");
        }
    }
}
