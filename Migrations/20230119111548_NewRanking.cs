using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class NewRanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "PassRating",
                table: "RankUpdate",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "PredictedAcc",
                table: "RankUpdate",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "PassRating",
                table: "DifficultyDescription",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "PredictedAcc",
                table: "DifficultyDescription",
                type: "real",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PassRating",
                table: "RankUpdate");

            migrationBuilder.DropColumn(
                name: "PredictedAcc",
                table: "RankUpdate");

            migrationBuilder.DropColumn(
                name: "PassRating",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "PredictedAcc",
                table: "DifficultyDescription");
        }
    }
}
