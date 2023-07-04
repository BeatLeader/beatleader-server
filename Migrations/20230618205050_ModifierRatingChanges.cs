using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ModifierRatingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NewModifiersRatingId",
                table: "LeaderboardChange",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OldModifiersRatingId",
                table: "LeaderboardChange",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardChange_NewModifiersRatingId",
                table: "LeaderboardChange",
                column: "NewModifiersRatingId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardChange_OldModifiersRatingId",
                table: "LeaderboardChange",
                column: "OldModifiersRatingId");

            migrationBuilder.AddForeignKey(
                name: "FK_LeaderboardChange_ModifiersRating_NewModifiersRatingId",
                table: "LeaderboardChange",
                column: "NewModifiersRatingId",
                principalTable: "ModifiersRating",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LeaderboardChange_ModifiersRating_OldModifiersRatingId",
                table: "LeaderboardChange",
                column: "OldModifiersRatingId",
                principalTable: "ModifiersRating",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaderboardChange_ModifiersRating_NewModifiersRatingId",
                table: "LeaderboardChange");

            migrationBuilder.DropForeignKey(
                name: "FK_LeaderboardChange_ModifiersRating_OldModifiersRatingId",
                table: "LeaderboardChange");

            migrationBuilder.DropIndex(
                name: "IX_LeaderboardChange_NewModifiersRatingId",
                table: "LeaderboardChange");

            migrationBuilder.DropIndex(
                name: "IX_LeaderboardChange_OldModifiersRatingId",
                table: "LeaderboardChange");

            migrationBuilder.DropColumn(
                name: "NewModifiersRatingId",
                table: "LeaderboardChange");

            migrationBuilder.DropColumn(
                name: "OldModifiersRatingId",
                table: "LeaderboardChange");
        }
    }
}
