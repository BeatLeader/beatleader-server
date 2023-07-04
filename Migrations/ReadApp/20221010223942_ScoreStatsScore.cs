using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class ScoreStatsScore : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OldScoreId",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Changer",
                table: "PlayerChange",
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

        protected override void Down(MigrationBuilder migrationBuilder)
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
                name: "Changer",
                table: "PlayerChange");
        }
    }
}
