using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ScorePlayCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerLeaderboardStats_Scores_OldScoreId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropIndex(
                name: "IX_PlayerLeaderboardStats_OldScoreId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "OldScoreId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Replay",
                table: "PlayerLeaderboardStats");

            migrationBuilder.AddColumn<int>(
                name: "PlayCount",
                table: "Scores",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PlayCount",
                table: "Leaderboards",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlayCount",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "PlayCount",
                table: "Leaderboards");

            migrationBuilder.AddColumn<int>(
                name: "OldScoreId",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Replay",
                table: "PlayerLeaderboardStats",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_OldScoreId",
                table: "PlayerLeaderboardStats",
                column: "OldScoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerLeaderboardStats_Scores_OldScoreId",
                table: "PlayerLeaderboardStats",
                column: "OldScoreId",
                principalTable: "Scores",
                principalColumn: "Id");
        }
    }
}
