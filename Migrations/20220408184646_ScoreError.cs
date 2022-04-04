using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class ScoreError : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TotalScore",
                table: "WinTracker",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FailedScores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaseScore = table.Column<int>(type: "int", nullable: false),
                    ModifiedScore = table.Column<int>(type: "int", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    Weight = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    Replay = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Modifiers = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BadCuts = table.Column<int>(type: "int", nullable: false),
                    MissedNotes = table.Column<int>(type: "int", nullable: false),
                    BombCuts = table.Column<int>(type: "int", nullable: false),
                    WallsHit = table.Column<int>(type: "int", nullable: false),
                    Pauses = table.Column<int>(type: "int", nullable: false),
                    FullCombo = table.Column<bool>(type: "bit", nullable: false),
                    Hmd = table.Column<int>(type: "int", nullable: false),
                    Timeset = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailedScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FailedScores_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FailedScores_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FailedScores_LeaderboardId",
                table: "FailedScores",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_FailedScores_PlayerId",
                table: "FailedScores",
                column: "PlayerId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FailedScores");

            migrationBuilder.DropColumn(
                name: "TotalScore",
                table: "WinTracker");
        }
    }
}
