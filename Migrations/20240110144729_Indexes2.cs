using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class Indexes2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Scores_Accuracy",
                table: "Scores",
                column: "Accuracy");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_Pp",
                table: "Scores",
                column: "Pp");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_Timepost",
                table: "Scores",
                column: "Timepost");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_Accuracy",
                table: "Scores");

            migrationBuilder.DropIndex(
                name: "IX_Scores_Pp",
                table: "Scores");

            migrationBuilder.DropIndex(
                name: "IX_Scores_Timepost",
                table: "Scores");
        }
    }
}
