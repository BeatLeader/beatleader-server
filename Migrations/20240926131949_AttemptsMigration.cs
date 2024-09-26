using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class AttemptsMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerLeaderboardStats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerLeaderboardStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ReplayOffsetsId = table.Column<int>(type: "int", nullable: true),
                    ScoreImprovementId = table.Column<int>(type: "int", nullable: true),
                    AccLeft = table.Column<float>(type: "real", nullable: false),
                    AccPP = table.Column<float>(type: "real", nullable: false),
                    AccRight = table.Column<float>(type: "real", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
                    AnonimusReplayWatched = table.Column<int>(type: "int", nullable: false),
                    AuthorizedReplayWatched = table.Column<int>(type: "int", nullable: false),
                    BadCuts = table.Column<int>(type: "int", nullable: false),
                    BaseScore = table.Column<int>(type: "int", nullable: false),
                    BombCuts = table.Column<int>(type: "int", nullable: false),
                    BonusPp = table.Column<float>(type: "real", nullable: false),
                    Controller = table.Column<int>(type: "int", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    FcAccuracy = table.Column<float>(type: "real", nullable: false),
                    FcPp = table.Column<float>(type: "real", nullable: false),
                    FullCombo = table.Column<bool>(type: "bit", nullable: false),
                    Hmd = table.Column<int>(type: "int", nullable: false),
                    LeftTiming = table.Column<float>(type: "real", nullable: false),
                    MaxCombo = table.Column<int>(type: "int", nullable: false),
                    MaxStreak = table.Column<int>(type: "int", nullable: true),
                    MissedNotes = table.Column<int>(type: "int", nullable: false),
                    ModifiedScore = table.Column<int>(type: "int", nullable: false),
                    Modifiers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PassPP = table.Column<float>(type: "real", nullable: false),
                    Pauses = table.Column<int>(type: "int", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlayerIdCopy = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Qualification = table.Column<bool>(type: "bit", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    Replay = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReplayCopy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RightTiming = table.Column<float>(type: "real", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    ScoreId = table.Column<int>(type: "int", nullable: true),
                    TechPP = table.Column<float>(type: "real", nullable: false),
                    Time = table.Column<float>(type: "real", nullable: false),
                    Timepost = table.Column<int>(type: "int", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    WallsHit = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerLeaderboardStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerLeaderboardStats_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayerLeaderboardStats_ReplayOffsets_ReplayOffsetsId",
                        column: x => x.ReplayOffsetsId,
                        principalTable: "ReplayOffsets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayerLeaderboardStats_ScoreImprovement_ScoreImprovementId",
                        column: x => x.ScoreImprovementId,
                        principalTable: "ScoreImprovement",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_LeaderboardId",
                table: "PlayerLeaderboardStats",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_ReplayCopy_PlayerIdCopy",
                table: "PlayerLeaderboardStats",
                columns: new[] { "ReplayCopy", "PlayerIdCopy" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_ReplayOffsetsId",
                table: "PlayerLeaderboardStats",
                column: "ReplayOffsetsId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_ScoreImprovementId",
                table: "PlayerLeaderboardStats",
                column: "ScoreImprovementId");
        }
    }
}
