using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.Storage
{
    /// <inheritdoc />
    public partial class ScoreIdIndex2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_PlayerId_ScoreId",
                table: "PlayerLeaderboardStats",
                columns: new[] { "PlayerId", "ScoreId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerLeaderboardStats_PlayerId_ScoreId",
                table: "PlayerLeaderboardStats");
        }
    }
}
