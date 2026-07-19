using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class RankedPlay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RankedPlaySeasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartDate = table.Column<int>(type: "int", nullable: false),
                    EndDate = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankedPlaySeasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RankedPlayMapBans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeasonId = table.Column<int>(type: "int", nullable: true),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BannedAt = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankedPlayMapBans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RankedPlayMapBans_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RankedPlayMapBans_RankedPlaySeasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "RankedPlaySeasons",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RankedPlayProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    SeasonId = table.Column<int>(type: "int", nullable: true),
                    MMR = table.Column<float>(type: "real", nullable: false),
                    PeakMMR = table.Column<float>(type: "real", nullable: false),
                    Wins = table.Column<int>(type: "int", nullable: false),
                    Losses = table.Column<int>(type: "int", nullable: false),
                    WinStreak = table.Column<int>(type: "int", nullable: false),
                    CalibrationMatchesPlayed = table.Column<int>(type: "int", nullable: false),
                    IsCalibrated = table.Column<bool>(type: "bit", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    TierDivision = table.Column<int>(type: "int", nullable: false),
                    LastMatchTime = table.Column<int>(type: "int", nullable: false),
                    DecayedMMR = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankedPlayProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RankedPlayProfiles_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RankedPlayProfiles_RankedPlaySeasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "RankedPlaySeasons",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RankedPlayMatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeasonId = table.Column<int>(type: "int", nullable: true),
                    PlayerAId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PlayerBId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PlayerAPreMatchMMR = table.Column<float>(type: "real", nullable: false),
                    PlayerBPreMatchMMR = table.Column<float>(type: "real", nullable: false),
                    PlayerAScore = table.Column<int>(type: "int", nullable: false),
                    PlayerBScore = table.Column<int>(type: "int", nullable: false),
                    PlayerAAccuracy = table.Column<float>(type: "real", nullable: false),
                    PlayerBAccuracy = table.Column<float>(type: "real", nullable: false),
                    PlayerAReplay = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlayerBReplay = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WinnerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Result = table.Column<int>(type: "int", nullable: false),
                    PlayerAMMRChange = table.Column<float>(type: "real", nullable: false),
                    PlayerBMMRChange = table.Column<float>(type: "real", nullable: false),
                    MapCandidatesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PromotedLeaderboardId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BlockedLeaderboardId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProfileAId = table.Column<int>(type: "int", nullable: true),
                    ProfileBId = table.Column<int>(type: "int", nullable: true),
                    Timestamp = table.Column<int>(type: "int", nullable: false),
                    Duration = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankedPlayMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RankedPlayMatches_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RankedPlayMatches_Players_PlayerAId",
                        column: x => x.PlayerAId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RankedPlayMatches_Players_PlayerBId",
                        column: x => x.PlayerBId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RankedPlayMatches_RankedPlayProfiles_ProfileAId",
                        column: x => x.ProfileAId,
                        principalTable: "RankedPlayProfiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RankedPlayMatches_RankedPlayProfiles_ProfileBId",
                        column: x => x.ProfileBId,
                        principalTable: "RankedPlayProfiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RankedPlayMatches_RankedPlaySeasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "RankedPlaySeasons",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayMapBans_LeaderboardId",
                table: "RankedPlayMapBans",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayMapBans_SeasonId_LeaderboardId",
                table: "RankedPlayMapBans",
                columns: new[] { "SeasonId", "LeaderboardId" },
                unique: true,
                filter: "[SeasonId] IS NOT NULL AND [LeaderboardId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayMatches_LeaderboardId",
                table: "RankedPlayMatches",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayMatches_PlayerAId_SeasonId",
                table: "RankedPlayMatches",
                columns: new[] { "PlayerAId", "SeasonId" });

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayMatches_PlayerBId_SeasonId",
                table: "RankedPlayMatches",
                columns: new[] { "PlayerBId", "SeasonId" });

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayMatches_ProfileAId",
                table: "RankedPlayMatches",
                column: "ProfileAId");

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayMatches_ProfileBId",
                table: "RankedPlayMatches",
                column: "ProfileBId");

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayMatches_SeasonId_Timestamp",
                table: "RankedPlayMatches",
                columns: new[] { "SeasonId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayProfiles_PlayerId_SeasonId",
                table: "RankedPlayProfiles",
                columns: new[] { "PlayerId", "SeasonId" },
                unique: true,
                filter: "[PlayerId] IS NOT NULL AND [SeasonId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RankedPlayProfiles_SeasonId_MMR",
                table: "RankedPlayProfiles",
                columns: new[] { "SeasonId", "MMR" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankedPlayMapBans");

            migrationBuilder.DropTable(
                name: "RankedPlayMatches");

            migrationBuilder.DropTable(
                name: "RankedPlayProfiles");

            migrationBuilder.DropTable(
                name: "RankedPlaySeasons");
        }
    }
}
