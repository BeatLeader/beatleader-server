using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ContextScoresImprovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScoreImprovementId",
                table: "ScoreContextExtensions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScoreContextExtensions_ScoreImprovementId",
                table: "ScoreContextExtensions",
                column: "ScoreImprovementId");

            migrationBuilder.AddForeignKey(
                name: "FK_ScoreContextExtensions_ScoreImprovement_ScoreImprovementId",
                table: "ScoreContextExtensions",
                column: "ScoreImprovementId",
                principalTable: "ScoreImprovement",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScoreContextExtensions_ScoreImprovement_ScoreImprovementId",
                table: "ScoreContextExtensions");

            migrationBuilder.DropIndex(
                name: "IX_ScoreContextExtensions_ScoreImprovementId",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "ScoreImprovementId",
                table: "ScoreContextExtensions");
        }
    }
}
