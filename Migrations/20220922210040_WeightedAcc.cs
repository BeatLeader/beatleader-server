using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class WeightedAcc : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AverageWeightedRankedAccuracy",
                table: "StatsHistory",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<float>(
                name: "AverageWeightedRankedAccuracy",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AverageWeightedRankedAccuracy",
                table: "StatsHistory");

            migrationBuilder.DropColumn(
                name: "AverageWeightedRankedAccuracy",
                table: "Stats");
        }
    }
}
