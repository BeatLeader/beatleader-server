using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class NewRanking3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ModifiersRatingId",
                table: "RankUpdate",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "TechRating",
                table: "RankUpdate",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "ModifiersRatingId",
                table: "RankQualification",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModifiersRatingId",
                table: "DifficultyDescription",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "TechRating",
                table: "DifficultyDescription",
                type: "real",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ModifiersRating",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FSPredictedAcc = table.Column<float>(type: "real", nullable: false),
                    FSPassRating = table.Column<float>(type: "real", nullable: false),
                    FSAccRating = table.Column<float>(type: "real", nullable: false),
                    FSTechRating = table.Column<float>(type: "real", nullable: false),
                    SSPredictedAcc = table.Column<float>(type: "real", nullable: false),
                    SSPassRating = table.Column<float>(type: "real", nullable: false),
                    SSAccRating = table.Column<float>(type: "real", nullable: false),
                    SSTechRating = table.Column<float>(type: "real", nullable: false),
                    SFPredictedAcc = table.Column<float>(type: "real", nullable: false),
                    SFPassRating = table.Column<float>(type: "real", nullable: false),
                    SFAccRating = table.Column<float>(type: "real", nullable: false),
                    SFTechRating = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModifiersRating", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankUpdate_ModifiersRatingId",
                table: "RankUpdate",
                column: "ModifiersRatingId");

            migrationBuilder.CreateIndex(
                name: "IX_RankQualification_ModifiersRatingId",
                table: "RankQualification",
                column: "ModifiersRatingId");

            migrationBuilder.CreateIndex(
                name: "IX_DifficultyDescription_ModifiersRatingId",
                table: "DifficultyDescription",
                column: "ModifiersRatingId");

            migrationBuilder.AddForeignKey(
                name: "FK_DifficultyDescription_ModifiersRating_ModifiersRatingId",
                table: "DifficultyDescription",
                column: "ModifiersRatingId",
                principalTable: "ModifiersRating",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RankQualification_ModifiersRating_ModifiersRatingId",
                table: "RankQualification",
                column: "ModifiersRatingId",
                principalTable: "ModifiersRating",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RankUpdate_ModifiersRating_ModifiersRatingId",
                table: "RankUpdate",
                column: "ModifiersRatingId",
                principalTable: "ModifiersRating",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DifficultyDescription_ModifiersRating_ModifiersRatingId",
                table: "DifficultyDescription");

            migrationBuilder.DropForeignKey(
                name: "FK_RankQualification_ModifiersRating_ModifiersRatingId",
                table: "RankQualification");

            migrationBuilder.DropForeignKey(
                name: "FK_RankUpdate_ModifiersRating_ModifiersRatingId",
                table: "RankUpdate");

            migrationBuilder.DropTable(
                name: "ModifiersRating");

            migrationBuilder.DropIndex(
                name: "IX_RankUpdate_ModifiersRatingId",
                table: "RankUpdate");

            migrationBuilder.DropIndex(
                name: "IX_RankQualification_ModifiersRatingId",
                table: "RankQualification");

            migrationBuilder.DropIndex(
                name: "IX_DifficultyDescription_ModifiersRatingId",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "ModifiersRatingId",
                table: "RankUpdate");

            migrationBuilder.DropColumn(
                name: "TechRating",
                table: "RankUpdate");

            migrationBuilder.DropColumn(
                name: "ModifiersRatingId",
                table: "RankQualification");

            migrationBuilder.DropColumn(
                name: "ModifiersRatingId",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "TechRating",
                table: "DifficultyDescription");
        }
    }
}
