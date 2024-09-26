using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.Storage
{
    /// <inheritdoc />
    public partial class LeaderboardStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReplayOffsets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Frames = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<int>(type: "int", nullable: false),
                    Walls = table.Column<int>(type: "int", nullable: false),
                    Heights = table.Column<int>(type: "int", nullable: false),
                    Pauses = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplayOffsets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScoreImprovement",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timeset = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    BonusPp = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    AccRight = table.Column<float>(type: "real", nullable: false),
                    AccLeft = table.Column<float>(type: "real", nullable: false),
                    AverageRankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    TotalPp = table.Column<float>(type: "real", nullable: false),
                    TotalRank = table.Column<int>(type: "int", nullable: false),
                    BadCuts = table.Column<int>(type: "int", nullable: false),
                    MissedNotes = table.Column<int>(type: "int", nullable: false),
                    BombCuts = table.Column<int>(type: "int", nullable: false),
                    WallsHit = table.Column<int>(type: "int", nullable: false),
                    Pauses = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreImprovement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerLeaderboardStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Time = table.Column<float>(type: "real", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Replay = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LeaderboardId = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    ScoreId = table.Column<int>(type: "int", nullable: true),
                    BaseScore = table.Column<int>(type: "int", nullable: false),
                    ModifiedScore = table.Column<int>(type: "int", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
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
                    Pauses = table.Column<int>(type: "int", nullable: false),
                    FullCombo = table.Column<bool>(type: "bit", nullable: false),
                    MaxCombo = table.Column<int>(type: "int", nullable: false),
                    FcAccuracy = table.Column<float>(type: "real", nullable: false),
                    FcPp = table.Column<float>(type: "real", nullable: false),
                    Hmd = table.Column<int>(type: "int", nullable: false),
                    Controller = table.Column<int>(type: "int", nullable: false),
                    AccRight = table.Column<float>(type: "real", nullable: false),
                    AccLeft = table.Column<float>(type: "real", nullable: false),
                    Timepost = table.Column<int>(type: "int", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthorizedReplayWatched = table.Column<int>(type: "int", nullable: false),
                    AnonimusReplayWatched = table.Column<int>(type: "int", nullable: false),
                    ReplayOffsetsId = table.Column<int>(type: "int", nullable: true),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaxStreak = table.Column<int>(type: "int", nullable: true),
                    LeftTiming = table.Column<float>(type: "real", nullable: false),
                    RightTiming = table.Column<float>(type: "real", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ScoreImprovementId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerLeaderboardStats", x => x.Id);
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
                name: "IX_PlayerLeaderboardStats_ReplayOffsetsId",
                table: "PlayerLeaderboardStats",
                column: "ReplayOffsetsId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_ScoreImprovementId",
                table: "PlayerLeaderboardStats",
                column: "ScoreImprovementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerLeaderboardStats");

            migrationBuilder.DropTable(
                name: "ReplayOffsets");

            migrationBuilder.DropTable(
                name: "ScoreImprovement");
        }
    }
}
