using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class MorePlayerStats : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AverageUnrankedAccuracy",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "AverageUnrankedRank",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "TopRankedAccuracy",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "TopUnrankedAccuracy",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "TotalRankedScore",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalUnrankedScore",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnrankedPlayCount",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AverageUnrankedAccuracy",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "AverageUnrankedRank",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "TopRankedAccuracy",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "TopUnrankedAccuracy",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "TotalRankedScore",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "TotalUnrankedScore",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "UnrankedPlayCount",
                table: "Stats");
        }
    }
}
