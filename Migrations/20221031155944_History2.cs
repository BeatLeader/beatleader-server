using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class History2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerScoreStatsHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timestamp = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    TotalScore = table.Column<long>(type: "bigint", nullable: false),
                    TotalUnrankedScore = table.Column<long>(type: "bigint", nullable: false),
                    TotalRankedScore = table.Column<long>(type: "bigint", nullable: false),
                    LastScoreTime = table.Column<int>(type: "int", nullable: false),
                    LastUnrankedScoreTime = table.Column<int>(type: "int", nullable: false),
                    LastRankedScoreTime = table.Column<int>(type: "int", nullable: false),
                    AverageRankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    AverageWeightedRankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    AverageUnrankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    AverageAccuracy = table.Column<float>(type: "real", nullable: false),
                    MedianRankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    MedianAccuracy = table.Column<float>(type: "real", nullable: false),
                    TopRankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    TopUnrankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    TopAccuracy = table.Column<float>(type: "real", nullable: false),
                    TopPp = table.Column<float>(type: "real", nullable: false),
                    TopBonusPP = table.Column<float>(type: "real", nullable: false),
                    PeakRank = table.Column<float>(type: "real", nullable: false),
                    RankedPlayCount = table.Column<int>(type: "int", nullable: false),
                    UnrankedPlayCount = table.Column<int>(type: "int", nullable: false),
                    TotalPlayCount = table.Column<int>(type: "int", nullable: false),
                    AverageRankedRank = table.Column<float>(type: "real", nullable: false),
                    AverageWeightedRankedRank = table.Column<float>(type: "real", nullable: false),
                    AverageUnrankedRank = table.Column<float>(type: "real", nullable: false),
                    AverageRank = table.Column<float>(type: "real", nullable: false),
                    SSPPlays = table.Column<int>(type: "int", nullable: false),
                    SSPlays = table.Column<int>(type: "int", nullable: false),
                    SPPlays = table.Column<int>(type: "int", nullable: false),
                    SPlays = table.Column<int>(type: "int", nullable: false),
                    APlays = table.Column<int>(type: "int", nullable: false),
                    TopPlatform = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TopHMD = table.Column<int>(type: "int", nullable: false),
                    DailyImprovements = table.Column<int>(type: "int", nullable: false),
                    ReplaysWatched = table.Column<int>(type: "int", nullable: false),
                    WatchedReplays = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerScoreStatsHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerScoreStatsHistory_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerScoreStatsHistory_PlayerId",
                table: "PlayerScoreStatsHistory",
                column: "PlayerId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerScoreStatsHistory");
        }
    }
}
