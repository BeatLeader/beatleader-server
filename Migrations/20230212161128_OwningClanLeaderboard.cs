using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class OwningClanLeaderboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwningClanId",
                table: "Leaderboards",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnedLeaderboardsCount",
                table: "Clans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Leaderboards_OwningClanId",
                table: "Leaderboards",
                column: "OwningClanId");

            migrationBuilder.AddForeignKey(
                name: "FK_Leaderboards_Clans_OwningClanId",
                table: "Leaderboards",
                column: "OwningClanId",
                principalTable: "Clans",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leaderboards_Clans_OwningClanId",
                table: "Leaderboards");

            migrationBuilder.DropIndex(
                name: "IX_Leaderboards_OwningClanId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "OwningClanId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "OwnedLeaderboardsCount",
                table: "Clans");
        }
    }
}
