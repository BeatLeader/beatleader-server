using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class ScoreImprovements : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AccLeft",
                table: "Scores",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "AccRight",
                table: "Scores",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "ScoreImprovementId",
                table: "Scores",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScoreImprovement",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    AccRight = table.Column<float>(type: "real", nullable: false),
                    AccLeft = table.Column<float>(type: "real", nullable: false),
                    AverageRankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    TotalPp = table.Column<float>(type: "real", nullable: false),
                    TotalRank = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreImprovement", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_ScoreImprovementId",
                table: "Scores",
                column: "ScoreImprovementId");

            migrationBuilder.AddForeignKey(
                name: "FK_Scores_ScoreImprovement_ScoreImprovementId",
                table: "Scores",
                column: "ScoreImprovementId",
                principalTable: "ScoreImprovement",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Scores_ScoreImprovement_ScoreImprovementId",
                table: "Scores");

            migrationBuilder.DropTable(
                name: "ScoreImprovement");

            migrationBuilder.DropIndex(
                name: "IX_Scores_ScoreImprovementId",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "AccLeft",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "AccRight",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "ScoreImprovementId",
                table: "Scores");
        }
    }
}
