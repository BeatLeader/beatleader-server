using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class StatisticMigrations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaderboardStatistics");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaderboardStatistics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccuracyTrackerId = table.Column<int>(type: "int", nullable: false),
                    HitTrackerId = table.Column<int>(type: "int", nullable: false),
                    ScoreGraphTrackerId = table.Column<int>(type: "int", nullable: false),
                    WinTrackerId = table.Column<int>(type: "int", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Relevant = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardStatistics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaderboardStatistics_AccuracyTracker_AccuracyTrackerId",
                        column: x => x.AccuracyTrackerId,
                        principalTable: "AccuracyTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaderboardStatistics_HitTracker_HitTrackerId",
                        column: x => x.HitTrackerId,
                        principalTable: "HitTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaderboardStatistics_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaderboardStatistics_ScoreGraphTracker_ScoreGraphTrackerId",
                        column: x => x.ScoreGraphTrackerId,
                        principalTable: "ScoreGraphTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaderboardStatistics_WinTracker_WinTrackerId",
                        column: x => x.WinTrackerId,
                        principalTable: "WinTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStatistics_AccuracyTrackerId",
                table: "LeaderboardStatistics",
                column: "AccuracyTrackerId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStatistics_HitTrackerId",
                table: "LeaderboardStatistics",
                column: "HitTrackerId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStatistics_LeaderboardId",
                table: "LeaderboardStatistics",
                column: "LeaderboardId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStatistics_ScoreGraphTrackerId",
                table: "LeaderboardStatistics",
                column: "ScoreGraphTrackerId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStatistics_WinTrackerId",
                table: "LeaderboardStatistics",
                column: "WinTrackerId");
        }
    }
}
