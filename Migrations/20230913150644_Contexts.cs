using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class Contexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_PlayerId_LeaderboardId",
                table: "Scores");

            migrationBuilder.AddColumn<int>(
                name: "ValidContexts",
                table: "Scores",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Context",
                table: "PlayerScoreStatsHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PlayerContextExtensions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Context = table.Column<int>(type: "int", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    AccPp = table.Column<float>(type: "real", nullable: false),
                    TechPp = table.Column<float>(type: "real", nullable: false),
                    PassPp = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    LastWeekPp = table.Column<float>(type: "real", nullable: false),
                    LastWeekRank = table.Column<int>(type: "int", nullable: false),
                    LastWeekCountryRank = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ScoreStatsId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerContextExtensions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerContextExtensions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerContextExtensions_Stats_ScoreStatsId",
                        column: x => x.ScoreStatsId,
                        principalTable: "Stats",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ScoreContextExtensions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Weight = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    BaseScore = table.Column<int>(type: "int", nullable: false),
                    ModifiedScore = table.Column<int>(type: "int", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    PassPP = table.Column<float>(type: "real", nullable: false),
                    AccPP = table.Column<float>(type: "real", nullable: false),
                    TechPP = table.Column<float>(type: "real", nullable: false),
                    BonusPp = table.Column<float>(type: "real", nullable: false),
                    Modifiers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ScoreId = table.Column<int>(type: "int", nullable: true),
                    Context = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreContextExtensions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoreContextExtensions_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScoreContextExtensions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScoreContextExtensions_Scores_ScoreId",
                        column: x => x.ScoreId,
                        principalTable: "Scores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_PlayerId_LeaderboardId_ValidContexts",
                table: "Scores",
                columns: new[] { "PlayerId", "LeaderboardId", "ValidContexts" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerContextExtensions_PlayerId_Context",
                table: "PlayerContextExtensions",
                columns: new[] { "PlayerId", "Context" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerContextExtensions_ScoreStatsId",
                table: "PlayerContextExtensions",
                column: "ScoreStatsId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreContextExtensions_LeaderboardId",
                table: "ScoreContextExtensions",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreContextExtensions_PlayerId_LeaderboardId_Context",
                table: "ScoreContextExtensions",
                columns: new[] { "PlayerId", "LeaderboardId", "Context" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScoreContextExtensions_ScoreId",
                table: "ScoreContextExtensions",
                column: "ScoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerContextExtensions");

            migrationBuilder.DropTable(
                name: "ScoreContextExtensions");

            migrationBuilder.DropIndex(
                name: "IX_Scores_PlayerId_LeaderboardId_ValidContexts",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "ValidContexts",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "Context",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_PlayerId_LeaderboardId",
                table: "Scores",
                columns: new[] { "PlayerId", "LeaderboardId" },
                unique: true);
        }
    }
}
