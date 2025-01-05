using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ScoresIndexWithLeaderboardId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Scores_LeaderboardId_Banned_ValidForGeneral",
                table: "Scores",
                columns: new[] { "LeaderboardId", "Banned", "ValidForGeneral" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_LeaderboardId_Banned_ValidForGeneral",
                table: "Scores");
        }
    }
}
