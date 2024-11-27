using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ValidForGeneral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ValidForGeneral",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Scores_PlayerId_LeaderboardId_ValidForGeneral",
                table: "Scores",
                columns: new[] { "PlayerId", "LeaderboardId", "ValidForGeneral" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_PlayerId_LeaderboardId_ValidForGeneral",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "ValidForGeneral",
                table: "Scores");
        }
    }
}
