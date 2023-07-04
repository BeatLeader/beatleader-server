using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ClanRankingCapturedLeaderboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClanRankingId",
                table: "Scores",
                type: "int",
                nullable: true);

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

            migrationBuilder.AddColumn<int>(
                name: "RankedPoolPercentCaptured",
                table: "Clans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ClanRanking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClanId = table.Column<int>(type: "int", nullable: false),
                    LastUpdateTime = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClanRank = table.Column<int>(type: "int", nullable: false),
                    ClanAverageRank = table.Column<float>(type: "real", nullable: false),
                    ClanPP = table.Column<float>(type: "real", nullable: false),
                    ClanAverageAccuracy = table.Column<float>(type: "real", nullable: false),
                    ClanTotalScore = table.Column<float>(type: "real", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanRanking", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClanRanking_Clans_ClanId",
                        column: x => x.ClanId,
                        principalTable: "Clans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClanRanking_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_ClanRankingId",
                table: "Scores",
                column: "ClanRankingId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_Leaderboards_Clans_ClanId",
                table: "Leaderboards",
                column: "ClanId",
                principalTable: "Clans",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Scores_ClanRanking_ClanRankingId",
                table: "Scores",
                column: "ClanRankingId",
                principalTable: "ClanRanking",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leaderboards_Clans_ClanId",
                table: "Leaderboards");

            migrationBuilder.DropForeignKey(
                name: "FK_Scores_ClanRanking_ClanRankingId",
                table: "Scores");

            migrationBuilder.DropTable(
                name: "ClanRanking");

            migrationBuilder.DropIndex(
                name: "IX_Scores_ClanRankingId",
                table: "Scores");

            migrationBuilder.DropIndex(
                name: "IX_Leaderboards_ClanId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "ClanRankingId",
                table: "Scores");

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
