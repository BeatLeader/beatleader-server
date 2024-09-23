using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.Storage
{
    /// <inheritdoc />
    public partial class HistoryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PlayerScoreStatsHistory_PlayerId_Context_Timestamp",
                table: "PlayerScoreStatsHistory",
                columns: new[] { "PlayerId", "Context", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerScoreStatsHistory_PlayerId_Context_Timestamp",
                table: "PlayerScoreStatsHistory");
        }
    }
}
