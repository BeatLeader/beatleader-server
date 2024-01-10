using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WatchingSessions_ScoreId",
                table: "WatchingSessions",
                column: "ScoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Stats_LastRankedScoreTime",
                table: "Stats",
                column: "LastRankedScoreTime");

            migrationBuilder.CreateIndex(
                name: "IX_Stats_RankedPlayCount",
                table: "Stats",
                column: "RankedPlayCount");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Banned_Pp_ScoreStatsId",
                table: "Players",
                columns: new[] { "Banned", "Pp", "ScoreStatsId" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_Rank",
                table: "Players",
                column: "Rank");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WatchingSessions_ScoreId",
                table: "WatchingSessions");

            migrationBuilder.DropIndex(
                name: "IX_Stats_LastRankedScoreTime",
                table: "Stats");

            migrationBuilder.DropIndex(
                name: "IX_Stats_RankedPlayCount",
                table: "Stats");

            migrationBuilder.DropIndex(
                name: "IX_Players_Banned_Pp_ScoreStatsId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Players_Rank",
                table: "Players");
        }
    }
}
