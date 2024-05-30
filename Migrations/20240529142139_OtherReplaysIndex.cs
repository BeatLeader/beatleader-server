using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class OtherReplaysIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlayerIdCopy",
                table: "PlayerLeaderboardStats",
                type: "nvarchar(25)",
                maxLength: 25,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReplayCopy",
                table: "PlayerLeaderboardStats",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_ReplayCopy_PlayerIdCopy",
                table: "PlayerLeaderboardStats",
                columns: new[] { "ReplayCopy", "PlayerIdCopy" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerLeaderboardStats_ReplayCopy_PlayerIdCopy",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "PlayerIdCopy",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "ReplayCopy",
                table: "PlayerLeaderboardStats");
        }
    }
}
