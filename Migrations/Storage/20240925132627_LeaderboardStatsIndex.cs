using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.Storage
{
    /// <inheritdoc />
    public partial class LeaderboardStatsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_PlayerId_LeaderboardId_Timeset",
                table: "PlayerLeaderboardStats",
                columns: new[] { "PlayerId", "LeaderboardId", "Timeset" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerLeaderboardStats_PlayerId_LeaderboardId_Timeset",
                table: "PlayerLeaderboardStats");
        }
    }
}
