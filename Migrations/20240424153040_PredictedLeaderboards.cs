using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class PredictedLeaderboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PredictedScores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaseScore = table.Column<int>(type: "int", nullable: false),
                    ModifiedScore = table.Column<int>(type: "int", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    BonusPp = table.Column<float>(type: "real", nullable: false),
                    PassPP = table.Column<float>(type: "real", nullable: false),
                    AccPP = table.Column<float>(type: "real", nullable: false),
                    TechPP = table.Column<float>(type: "real", nullable: false),
                    Qualification = table.Column<bool>(type: "bit", nullable: false),
                    Weight = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    Modifiers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BadCuts = table.Column<int>(type: "int", nullable: false),
                    MissedNotes = table.Column<int>(type: "int", nullable: false),
                    BombCuts = table.Column<int>(type: "int", nullable: false),
                    WallsHit = table.Column<int>(type: "int", nullable: false),
                    FullCombo = table.Column<bool>(type: "bit", nullable: false),
                    MaxCombo = table.Column<int>(type: "int", nullable: false),
                    FcAccuracy = table.Column<float>(type: "real", nullable: false),
                    FcPp = table.Column<float>(type: "real", nullable: false),
                    AccRight = table.Column<float>(type: "real", nullable: false),
                    AccLeft = table.Column<float>(type: "real", nullable: false),
                    Timepost = table.Column<int>(type: "int", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictedScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PredictedScores_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PredictedScores_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PredictedScores_Accuracy",
                table: "PredictedScores",
                column: "Accuracy");

            migrationBuilder.CreateIndex(
                name: "IX_PredictedScores_LeaderboardId",
                table: "PredictedScores",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_PredictedScores_PlayerId",
                table: "PredictedScores",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PredictedScores_PlayerId_LeaderboardId",
                table: "PredictedScores",
                columns: new[] { "PlayerId", "LeaderboardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PredictedScores_PlayerId_Qualification_Pp",
                table: "PredictedScores",
                columns: new[] { "PlayerId", "Qualification", "Pp" });

            migrationBuilder.CreateIndex(
                name: "IX_PredictedScores_Pp",
                table: "PredictedScores",
                column: "Pp");

            migrationBuilder.CreateIndex(
                name: "IX_PredictedScores_Qualification_Pp",
                table: "PredictedScores",
                columns: new[] { "Qualification", "Pp" });

            migrationBuilder.CreateIndex(
                name: "IX_PredictedScores_Timepost",
                table: "PredictedScores",
                column: "Timepost");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PredictedScores");
        }
    }
}
