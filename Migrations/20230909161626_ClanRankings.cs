using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ClanRankings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClanId",
                table: "Leaderboards",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ClanRankingContested",
                table: "Leaderboards",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<float>(
                name: "RankedPoolPercentCaptured",
                table: "Clans",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.CreateTable(
                name: "ClanRanking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClanId = table.Column<int>(type: "int", nullable: true),
                    LastUpdateTime = table.Column<int>(type: "int", nullable: false),
                    AverageRank = table.Column<float>(type: "real", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    AverageAccuracy = table.Column<float>(type: "real", nullable: false),
                    TotalScore = table.Column<float>(type: "real", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanRanking", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClanRanking_Clans_ClanId",
                        column: x => x.ClanId,
                        principalTable: "Clans",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClanRanking_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClanRankingScore",
                columns: table => new
                {
                    AssociatedClanRankingsId = table.Column<int>(type: "int", nullable: false),
                    AssociatedScoresId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanRankingScore", x => new { x.AssociatedClanRankingsId, x.AssociatedScoresId });
                    table.ForeignKey(
                        name: "FK_ClanRankingScore_ClanRanking_AssociatedClanRankingsId",
                        column: x => x.AssociatedClanRankingsId,
                        principalTable: "ClanRanking",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClanRankingScore_Scores_AssociatedScoresId",
                        column: x => x.AssociatedScoresId,
                        principalTable: "Scores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leaderboards_ClanId",
                table: "Leaderboards",
                column: "ClanId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanRanking_ClanId",
                table: "ClanRanking",
                column: "ClanId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanRanking_LeaderboardId",
                table: "ClanRanking",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanRankingScore_AssociatedScoresId",
                table: "ClanRankingScore",
                column: "AssociatedScoresId");

            migrationBuilder.AddForeignKey(
                name: "FK_Leaderboards_Clans_ClanId",
                table: "Leaderboards",
                column: "ClanId",
                principalTable: "Clans",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leaderboards_Clans_ClanId",
                table: "Leaderboards");

            migrationBuilder.DropTable(
                name: "ClanRankingScore");

            migrationBuilder.DropTable(
                name: "ClanRanking");

            migrationBuilder.DropIndex(
                name: "IX_Leaderboards_ClanId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "ClanId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "ClanRankingContested",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "RankedPoolPercentCaptured",
                table: "Clans");
        }
    }
}
