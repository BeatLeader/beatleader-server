using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class StatisticHistory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "APlays",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "MedianAccuracy",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "MedianRankedAccuracy",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "SPPlays",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SPlays",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SSPPlays",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SSPlays",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "TopAccuracy",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "TopPp",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "StatsHistoryId",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StatsHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Pp = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rank = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CountryRank = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalScore = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AverageRankedAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TopAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TopPp = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AverageAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MedianAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MedianRankedAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalPlayCount = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RankedPlayCount = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReplaysWatched = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatsHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Players_StatsHistoryId",
                table: "Players",
                column: "StatsHistoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_StatsHistory_StatsHistoryId",
                table: "Players",
                column: "StatsHistoryId",
                principalTable: "StatsHistory",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_StatsHistory_StatsHistoryId",
                table: "Players");

            migrationBuilder.DropTable(
                name: "StatsHistory");

            migrationBuilder.DropIndex(
                name: "IX_Players_StatsHistoryId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "APlays",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "MedianAccuracy",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "MedianRankedAccuracy",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "SPPlays",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "SPlays",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "SSPPlays",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "SSPlays",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "TopAccuracy",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "TopPp",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "StatsHistoryId",
                table: "Players");
        }
    }
}
