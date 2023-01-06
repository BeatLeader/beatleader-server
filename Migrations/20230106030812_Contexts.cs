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
            migrationBuilder.DropColumn(
                name: "TimesetMig",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "TimesetMig",
                table: "ScoreImprovement");

            migrationBuilder.DropColumn(
                name: "TimesetMig",
                table: "FailedScores");

            migrationBuilder.AddColumn<int>(
                name: "LeaderboardType",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AltPlayerId",
                table: "PlayerScoreStatsHistory",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AltBoards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeaderboardType = table.Column<int>(type: "int", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Plays = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AltBoards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AltBoards_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AltPlayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    LeaderboardType = table.Column<int>(type: "int", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    LastWeekPp = table.Column<float>(type: "real", nullable: false),
                    LastWeekRank = table.Column<int>(type: "int", nullable: false),
                    LastWeekCountryRank = table.Column<int>(type: "int", nullable: false),
                    ScoreStatsId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AltPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AltPlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AltPlayers_Stats_ScoreStatsId",
                        column: x => x.ScoreStatsId,
                        principalTable: "Stats",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SecondaryScores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    BaseScore = table.Column<int>(type: "int", nullable: false),
                    ModifiedScore = table.Column<int>(type: "int", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    BonusPp = table.Column<float>(type: "real", nullable: false),
                    Qualification = table.Column<bool>(type: "bit", nullable: false),
                    Weight = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    Replay = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Modifiers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BadCuts = table.Column<int>(type: "int", nullable: false),
                    MissedNotes = table.Column<int>(type: "int", nullable: false),
                    BombCuts = table.Column<int>(type: "int", nullable: false),
                    WallsHit = table.Column<int>(type: "int", nullable: false),
                    Pauses = table.Column<int>(type: "int", nullable: false),
                    FullCombo = table.Column<bool>(type: "bit", nullable: false),
                    MaxCombo = table.Column<int>(type: "int", nullable: false),
                    FcAccuracy = table.Column<float>(type: "real", nullable: false),
                    FcPp = table.Column<float>(type: "real", nullable: false),
                    Hmd = table.Column<int>(type: "int", nullable: false),
                    Controller = table.Column<int>(type: "int", nullable: false),
                    AccRight = table.Column<float>(type: "real", nullable: false),
                    AccLeft = table.Column<float>(type: "real", nullable: false),
                    Timeset = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timepost = table.Column<int>(type: "int", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AuthorizedReplayWatched = table.Column<int>(type: "int", nullable: false),
                    AnonimusReplayWatched = table.Column<int>(type: "int", nullable: false),
                    ReplayOffsetsId = table.Column<int>(type: "int", nullable: true),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaxStreak = table.Column<int>(type: "int", nullable: false),
                    LeftTiming = table.Column<float>(type: "real", nullable: false),
                    RightTiming = table.Column<float>(type: "real", nullable: false),
                    ScoreImprovementId = table.Column<int>(type: "int", nullable: true),
                    Banned = table.Column<bool>(type: "bit", nullable: false),
                    Suspicious = table.Column<bool>(type: "bit", nullable: false),
                    IgnoreForStats = table.Column<bool>(type: "bit", nullable: false),
                    Migrated = table.Column<bool>(type: "bit", nullable: false),
                    RankVotingScoreId = table.Column<int>(type: "int", nullable: true),
                    MetadataId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecondaryScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecondaryScores_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SecondaryScores_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SecondaryScores_RankVotings_RankVotingScoreId",
                        column: x => x.RankVotingScoreId,
                        principalTable: "RankVotings",
                        principalColumn: "ScoreId");
                    table.ForeignKey(
                        name: "FK_SecondaryScores_ReplayOffsets_ReplayOffsetsId",
                        column: x => x.ReplayOffsetsId,
                        principalTable: "ReplayOffsets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SecondaryScores_ScoreImprovement_ScoreImprovementId",
                        column: x => x.ScoreImprovementId,
                        principalTable: "ScoreImprovement",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SecondaryScores_ScoreMetadata_MetadataId",
                        column: x => x.MetadataId,
                        principalTable: "ScoreMetadata",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AltScores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScoreId = table.Column<int>(type: "int", nullable: true),
                    ScoreImprovementId = table.Column<int>(type: "int", nullable: true),
                    Weight = table.Column<float>(type: "real", nullable: false),
                    Modifiers = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    BaseScore = table.Column<int>(type: "int", nullable: false),
                    ModifiedScore = table.Column<int>(type: "int", nullable: false),
                    Timeset = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    BonusPp = table.Column<float>(type: "real", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AltPlayerId = table.Column<int>(type: "int", nullable: true),
                    AltBoardId = table.Column<int>(type: "int", nullable: false),
                    LeaderboardType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AltScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AltScores_AltBoards_AltBoardId",
                        column: x => x.AltBoardId,
                        principalTable: "AltBoards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AltScores_AltPlayers_AltPlayerId",
                        column: x => x.AltPlayerId,
                        principalTable: "AltPlayers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AltScores_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AltScores_ScoreImprovement_ScoreImprovementId",
                        column: x => x.ScoreImprovementId,
                        principalTable: "ScoreImprovement",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AltScores_SecondaryScores_ScoreId",
                        column: x => x.ScoreId,
                        principalTable: "SecondaryScores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerScoreStatsHistory_AltPlayerId",
                table: "PlayerScoreStatsHistory",
                column: "AltPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AltBoards_LeaderboardId",
                table: "AltBoards",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_AltPlayers_PlayerId",
                table: "AltPlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AltPlayers_ScoreStatsId",
                table: "AltPlayers",
                column: "ScoreStatsId");

            migrationBuilder.CreateIndex(
                name: "IX_AltScores_AltBoardId",
                table: "AltScores",
                column: "AltBoardId");

            migrationBuilder.CreateIndex(
                name: "IX_AltScores_AltPlayerId",
                table: "AltScores",
                column: "AltPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AltScores_PlayerId",
                table: "AltScores",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AltScores_PlayerId_LeaderboardId",
                table: "AltScores",
                columns: new[] { "PlayerId", "LeaderboardId" });

            migrationBuilder.CreateIndex(
                name: "IX_AltScores_ScoreId",
                table: "AltScores",
                column: "ScoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AltScores_ScoreImprovementId",
                table: "AltScores",
                column: "ScoreImprovementId");

            migrationBuilder.CreateIndex(
                name: "IX_SecondaryScores_LeaderboardId",
                table: "SecondaryScores",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_SecondaryScores_MetadataId",
                table: "SecondaryScores",
                column: "MetadataId");

            migrationBuilder.CreateIndex(
                name: "IX_SecondaryScores_PlayerId",
                table: "SecondaryScores",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_SecondaryScores_RankVotingScoreId",
                table: "SecondaryScores",
                column: "RankVotingScoreId");

            migrationBuilder.CreateIndex(
                name: "IX_SecondaryScores_ReplayOffsetsId",
                table: "SecondaryScores",
                column: "ReplayOffsetsId");

            migrationBuilder.CreateIndex(
                name: "IX_SecondaryScores_ScoreImprovementId",
                table: "SecondaryScores",
                column: "ScoreImprovementId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerScoreStatsHistory_AltPlayers_AltPlayerId",
                table: "PlayerScoreStatsHistory",
                column: "AltPlayerId",
                principalTable: "AltPlayers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerScoreStatsHistory_AltPlayers_AltPlayerId",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.DropTable(
                name: "AltScores");

            migrationBuilder.DropTable(
                name: "AltBoards");

            migrationBuilder.DropTable(
                name: "AltPlayers");

            migrationBuilder.DropTable(
                name: "SecondaryScores");

            migrationBuilder.DropIndex(
                name: "IX_PlayerScoreStatsHistory_AltPlayerId",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.DropColumn(
                name: "LeaderboardType",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "AltPlayerId",
                table: "PlayerScoreStatsHistory");

            migrationBuilder.AddColumn<int>(
                name: "TimesetMig",
                table: "Scores",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TimesetMig",
                table: "ScoreImprovement",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TimesetMig",
                table: "FailedScores",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
