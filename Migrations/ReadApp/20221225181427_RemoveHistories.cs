using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    /// <inheritdoc />
    public partial class RemoveHistories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
                name: "Histories",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "StatsHistoryId",
                table: "Players");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Histories",
                table: "Players",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

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
                    AverageAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AverageRankedAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AverageWeightedRankedAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AverageWeightedRankedRank = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CountryRank = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MedianAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MedianRankedAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Pp = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rank = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RankedPlayCount = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReplaysWatched = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TopAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TopPp = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalPlayCount = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalScore = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
    }
}
