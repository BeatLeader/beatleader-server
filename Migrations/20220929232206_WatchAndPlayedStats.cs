using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class WatchAndPlayedStats : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WatchedReplays",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReplayWatched",
                table: "Scores",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PlayerLeaderboardStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Time = table.Column<float>(type: "real", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerLeaderboardStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerLeaderboardStats_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WatchingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScoreId = table.Column<int>(type: "int", nullable: false),
                    IPHash = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchingSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_LeaderboardId",
                table: "PlayerLeaderboardStats",
                column: "LeaderboardId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerLeaderboardStats");

            migrationBuilder.DropTable(
                name: "WatchingSessions");

            migrationBuilder.DropColumn(
                name: "WatchedReplays",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "ReplayWatched",
                table: "Scores");
        }
    }
}
