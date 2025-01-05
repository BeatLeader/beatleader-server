using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ScoresIndexWithTimeset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Scores_PlayerId_Banned_ValidForGeneral_Pp_Timepost",
                table: "Scores",
                columns: new[] { "PlayerId", "Banned", "ValidForGeneral", "Pp", "Timepost" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_PlayerId_Banned_ValidForGeneral_Pp_Timepost",
                table: "Scores");
        }
    }
}
