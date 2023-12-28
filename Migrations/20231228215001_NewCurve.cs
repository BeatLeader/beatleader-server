using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class NewCurve : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "NewLinearRating",
                table: "QualificationChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "NewPatternRating",
                table: "QualificationChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "OldLinearRating",
                table: "QualificationChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "OldPatternRating",
                table: "QualificationChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "NewLinearRating",
                table: "LeaderboardChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "NewPatternRating",
                table: "LeaderboardChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "OldLinearRating",
                table: "LeaderboardChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "OldPatternRating",
                table: "LeaderboardChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "LinearRating",
                table: "DifficultyDescription",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "PatternRating",
                table: "DifficultyDescription",
                type: "real",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CurvePoint",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    X = table.Column<float>(type: "real", nullable: false),
                    Y = table.Column<float>(type: "real", nullable: false),
                    DifficultyId = table.Column<int>(type: "int", nullable: true),
                    FSRatingId = table.Column<int>(type: "int", nullable: true),
                    SSRatingId = table.Column<int>(type: "int", nullable: true),
                    SFRatingId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurvePoint", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurvePoint_DifficultyDescription_DifficultyId",
                        column: x => x.DifficultyId,
                        principalTable: "DifficultyDescription",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CurvePoint_ModifiersRating_FSRatingId",
                        column: x => x.FSRatingId,
                        principalTable: "ModifiersRating",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CurvePoint_ModifiersRating_SFRatingId",
                        column: x => x.SFRatingId,
                        principalTable: "ModifiersRating",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CurvePoint_ModifiersRating_SSRatingId",
                        column: x => x.SSRatingId,
                        principalTable: "ModifiersRating",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CurvePoint_DifficultyId",
                table: "CurvePoint",
                column: "DifficultyId");

            migrationBuilder.CreateIndex(
                name: "IX_CurvePoint_FSRatingId",
                table: "CurvePoint",
                column: "FSRatingId");

            migrationBuilder.CreateIndex(
                name: "IX_CurvePoint_SFRatingId",
                table: "CurvePoint",
                column: "SFRatingId");

            migrationBuilder.CreateIndex(
                name: "IX_CurvePoint_SSRatingId",
                table: "CurvePoint",
                column: "SSRatingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurvePoint");

            migrationBuilder.DropColumn(
                name: "NewLinearRating",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "NewPatternRating",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "OldLinearRating",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "OldPatternRating",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "NewLinearRating",
                table: "LeaderboardChange");

            migrationBuilder.DropColumn(
                name: "NewPatternRating",
                table: "LeaderboardChange");

            migrationBuilder.DropColumn(
                name: "OldLinearRating",
                table: "LeaderboardChange");

            migrationBuilder.DropColumn(
                name: "OldPatternRating",
                table: "LeaderboardChange");

            migrationBuilder.DropColumn(
                name: "LinearRating",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "PatternRating",
                table: "DifficultyDescription");
        }
    }
}
