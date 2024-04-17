using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class WebpAvatarAndPartPPHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AccPp",
                table: "PlayerScoreStatsHistory",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "PassPp",
                table: "PlayerScoreStatsHistory",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "TechPp",
                table: "PlayerScoreStatsHistory",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<string>(
                name: "WebAvatar",
                table: "Players",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccPp",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.DropColumn(
                name: "PassPp",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.DropColumn(
                name: "TechPp",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.DropColumn(
                name: "WebAvatar",
                table: "Players");
        }
    }
}
