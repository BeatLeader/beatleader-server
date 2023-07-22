using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class VotesInLeaderboard : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NegativeVotes",
                table: "Leaderboards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PositiveVotes",
                table: "Leaderboards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StarVotes",
                table: "Leaderboards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "VoteStars",
                table: "Leaderboards",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NegativeVotes",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "PositiveVotes",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "StarVotes",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "VoteStars",
                table: "Leaderboards");
        }
    }
}
