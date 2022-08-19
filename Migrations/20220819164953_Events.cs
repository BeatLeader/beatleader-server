using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class Events : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventRankings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EndDate = table.Column<int>(type: "int", nullable: false),
                    PlaylistId = table.Column<int>(type: "int", nullable: false),
                    Image = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRankings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventPlayer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    EventRankingId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventPlayer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventPlayer_EventRankings_EventRankingId",
                        column: x => x.EventRankingId,
                        principalTable: "EventRankings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventPlayer_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventRankingLeaderboard",
                columns: table => new
                {
                    EventsId = table.Column<int>(type: "int", nullable: false),
                    LeaderboardsId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRankingLeaderboard", x => new { x.EventsId, x.LeaderboardsId });
                    table.ForeignKey(
                        name: "FK_EventRankingLeaderboard_EventRankings_EventsId",
                        column: x => x.EventsId,
                        principalTable: "EventRankings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventRankingLeaderboard_Leaderboards_LeaderboardsId",
                        column: x => x.LeaderboardsId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventPlayer_EventRankingId",
                table: "EventPlayer",
                column: "EventRankingId");

            migrationBuilder.CreateIndex(
                name: "IX_EventPlayer_PlayerId",
                table: "EventPlayer",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_EventRankingLeaderboard_LeaderboardsId",
                table: "EventRankingLeaderboard",
                column: "LeaderboardsId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventPlayer");

            migrationBuilder.DropTable(
                name: "EventRankingLeaderboard");

            migrationBuilder.DropTable(
                name: "EventRankings");
        }
    }
}
