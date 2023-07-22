using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class RevertLBOptionality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Scores_Leaderboards_LeaderboardId",
                table: "Scores");

            migrationBuilder.DropForeignKey(
                name: "FK_Scores_Players_PlayerId",
                table: "Scores");

            migrationBuilder.DropIndex(
                name: "IX_Scores_PlayerId_LeaderboardId",
                table: "Scores");

            migrationBuilder.AlterColumn<string>(
                name: "PlayerId",
                table: "Scores",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LeaderboardId",
                table: "Scores",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Scores_PlayerId_LeaderboardId",
                table: "Scores",
                columns: new[] { "PlayerId", "LeaderboardId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Scores_Leaderboards_LeaderboardId",
                table: "Scores",
                column: "LeaderboardId",
                principalTable: "Leaderboards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Scores_Players_PlayerId",
                table: "Scores",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Scores_Leaderboards_LeaderboardId",
                table: "Scores");

            migrationBuilder.DropForeignKey(
                name: "FK_Scores_Players_PlayerId",
                table: "Scores");

            migrationBuilder.DropIndex(
                name: "IX_Scores_PlayerId_LeaderboardId",
                table: "Scores");

            migrationBuilder.AlterColumn<string>(
                name: "PlayerId",
                table: "Scores",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "LeaderboardId",
                table: "Scores",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_PlayerId_LeaderboardId",
                table: "Scores",
                columns: new[] { "PlayerId", "LeaderboardId" },
                unique: true,
                filter: "[PlayerId] IS NOT NULL AND [LeaderboardId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Scores_Leaderboards_LeaderboardId",
                table: "Scores",
                column: "LeaderboardId",
                principalTable: "Leaderboards",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Scores_Players_PlayerId",
                table: "Scores",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id");
        }
    }
}
