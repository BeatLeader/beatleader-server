using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class Indexes1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Scores_Banned_Qualification_Pp",
                table: "Scores",
                columns: new[] { "Banned", "Qualification", "Pp" });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_PlayerId_Banned_Qualification_Pp",
                table: "Scores",
                columns: new[] { "PlayerId", "Banned", "Qualification", "Pp" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_Banned",
                table: "Players",
                column: "Banned");

            migrationBuilder.CreateIndex(
                name: "IX_DifficultyDescription_Status",
                table: "DifficultyDescription",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_Banned_Qualification_Pp",
                table: "Scores");

            migrationBuilder.DropIndex(
                name: "IX_Scores_PlayerId_Banned_Qualification_Pp",
                table: "Scores");

            migrationBuilder.DropIndex(
                name: "IX_Players_Banned",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_DifficultyDescription_Status",
                table: "DifficultyDescription");
        }
    }
}
