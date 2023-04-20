using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class Achievements1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AchievementDescriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Link = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementDescriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SurveyResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SurveyId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyResponses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AchievementLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Image = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SmallImage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DetailedDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<float>(type: "real", nullable: true),
                    Level = table.Column<int>(type: "int", nullable: false),
                    AchievementDescriptionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AchievementLevels_AchievementDescriptions_AchievementDescriptionId",
                        column: x => x.AchievementDescriptionId,
                        principalTable: "AchievementDescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AchievementDescriptionId = table.Column<int>(type: "int", nullable: false),
                    LevelId = table.Column<int>(type: "int", nullable: true),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Achievements_AchievementDescriptions_AchievementDescriptionId",
                        column: x => x.AchievementDescriptionId,
                        principalTable: "AchievementDescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Achievements_AchievementLevels_LevelId",
                        column: x => x.LevelId,
                        principalTable: "AchievementLevels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Achievements_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AchievementLevels_AchievementDescriptionId",
                table: "AchievementLevels",
                column: "AchievementDescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_AchievementDescriptionId",
                table: "Achievements",
                column: "AchievementDescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_LevelId",
                table: "Achievements",
                column: "LevelId");

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_PlayerId",
                table: "Achievements",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Achievements");

            migrationBuilder.DropTable(
                name: "SurveyResponses");

            migrationBuilder.DropTable(
                name: "AchievementLevels");

            migrationBuilder.DropTable(
                name: "AchievementDescriptions");
        }
    }
}
