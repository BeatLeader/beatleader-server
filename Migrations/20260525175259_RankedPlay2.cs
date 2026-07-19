using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class RankedPlay2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RankedPlayMatches_Leaderboards_LeaderboardId",
                table: "RankedPlayMatches");

            migrationBuilder.DropIndex(
                name: "IX_RankedPlayMatches_LeaderboardId",
                table: "RankedPlayMatches");

            migrationBuilder.DropColumn(
                name: "BlockedLeaderboardId",
                table: "RankedPlayMatches");

            migrationBuilder.DropColumn(
                name: "LeaderboardId",
                table: "RankedPlayMatches");

            migrationBuilder.DropColumn(
                name: "MapCandidatesJson",
                table: "RankedPlayMatches");

            migrationBuilder.DropColumn(
                name: "PlayerAAccuracy",
                table: "RankedPlayMatches");

            migrationBuilder.DropColumn(
                name: "PlayerAReplay",
                table: "RankedPlayMatches");

            migrationBuilder.DropColumn(
                name: "PlayerBAccuracy",
                table: "RankedPlayMatches");

            migrationBuilder.DropColumn(
                name: "PlayerBReplay",
                table: "RankedPlayMatches");

            migrationBuilder.DropColumn(
                name: "PromotedLeaderboardId",
                table: "RankedPlayMatches");

            migrationBuilder.RenameColumn(
                name: "WinStreak",
                table: "RankedPlayProfiles",
                newName: "Draws");

            migrationBuilder.RenameColumn(
                name: "PlayerBScore",
                table: "RankedPlayMatches",
                newName: "PlayerBGamesWon");

            migrationBuilder.RenameColumn(
                name: "PlayerAScore",
                table: "RankedPlayMatches",
                newName: "PlayerAGamesWon");

            migrationBuilder.AddColumn<int>(
                name: "GrandmasterRank",
                table: "RankedPlayProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DrawnGames",
                table: "RankedPlayMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RankedPlayGames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchId = table.Column<int>(type: "int", nullable: false),
                    RoundNumber = table.Column<int>(type: "int", nullable: false),
                    HandJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlayerADiscardLeaderboardId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlayerBDiscardLeaderboardId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReplacementsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PickerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PlayerAScore = table.Column<int>(type: "int", nullable: false),
                    PlayerBScore = table.Column<int>(type: "int", nullable: false),
                    PlayerAFinalScore = table.Column<int>(type: "int", nullable: false),
                    PlayerBFinalScore = table.Column<int>(type: "int", nullable: false),
                    PlayerAAccuracy = table.Column<float>(type: "real", nullable: false),
                    PlayerBAccuracy = table.Column<float>(type: "real", nullable: false),
                    PlayerAFailed = table.Column<bool>(type: "bit", nullable: false),
                    PlayerBFailed = table.Column<bool>(type: "bit", nullable: false),
                    PlayerAForfeit = table.Column<bool>(type: "bit", nullable: false),
                    PlayerBForfeit = table.Column<bool>(type: "bit", nullable: false),
                    PlayerAReplay = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlayerBReplay = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WinnerId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankedPlayGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RankedPlayGames_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RankedPlayGames_RankedPlayMatches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "RankedPlayMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayGames_LeaderboardId",
                table: "RankedPlayGames",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayGames_MatchId_RoundNumber",
                table: "RankedPlayGames",
                columns: new[] { "MatchId", "RoundNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankedPlayGames");

            migrationBuilder.DropColumn(
                name: "GrandmasterRank",
                table: "RankedPlayProfiles");

            migrationBuilder.DropColumn(
                name: "DrawnGames",
                table: "RankedPlayMatches");

            migrationBuilder.RenameColumn(
                name: "Draws",
                table: "RankedPlayProfiles",
                newName: "WinStreak");

            migrationBuilder.RenameColumn(
                name: "PlayerBGamesWon",
                table: "RankedPlayMatches",
                newName: "PlayerBScore");

            migrationBuilder.RenameColumn(
                name: "PlayerAGamesWon",
                table: "RankedPlayMatches",
                newName: "PlayerAScore");

            migrationBuilder.AddColumn<string>(
                name: "BlockedLeaderboardId",
                table: "RankedPlayMatches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaderboardId",
                table: "RankedPlayMatches",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MapCandidatesJson",
                table: "RankedPlayMatches",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<float>(
                name: "PlayerAAccuracy",
                table: "RankedPlayMatches",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<string>(
                name: "PlayerAReplay",
                table: "RankedPlayMatches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "PlayerBAccuracy",
                table: "RankedPlayMatches",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<string>(
                name: "PlayerBReplay",
                table: "RankedPlayMatches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromotedLeaderboardId",
                table: "RankedPlayMatches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayMatches_LeaderboardId",
                table: "RankedPlayMatches",
                column: "LeaderboardId");

            migrationBuilder.AddForeignKey(
                name: "FK_RankedPlayMatches_Leaderboards_LeaderboardId",
                table: "RankedPlayMatches",
                column: "LeaderboardId",
                principalTable: "Leaderboards",
                principalColumn: "Id");
        }
    }
}
