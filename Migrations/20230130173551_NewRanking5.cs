using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class NewRanking5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "NewAccRating",
                table: "LeaderboardChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "NewPassRating",
                table: "LeaderboardChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "NewTechRating",
                table: "LeaderboardChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "OldAccRating",
                table: "LeaderboardChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "OldPassRating",
                table: "LeaderboardChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "OldTechRating",
                table: "LeaderboardChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewAccRating",
                table: "LeaderboardChange");

            migrationBuilder.DropColumn(
                name: "NewPassRating",
                table: "LeaderboardChange");

            migrationBuilder.DropColumn(
                name: "NewTechRating",
                table: "LeaderboardChange");

            migrationBuilder.DropColumn(
                name: "OldAccRating",
                table: "LeaderboardChange");

            migrationBuilder.DropColumn(
                name: "OldPassRating",
                table: "LeaderboardChange");

            migrationBuilder.DropColumn(
                name: "OldTechRating",
                table: "LeaderboardChange");
        }
    }
}
