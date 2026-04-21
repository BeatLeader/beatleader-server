using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class FavoriteMaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "Speed",
                table: "Scores",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<string>(
                name: "LeaderboardId",
                table: "RankVotings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FansCount",
                table: "Leaderboards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FavoriteMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    RankVotingId = table.Column<int>(type: "int", nullable: true),
                    Aspect = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavoriteMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FavoriteMaps_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FavoriteMaps_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FavoriteMaps_RankVotings_RankVotingId",
                        column: x => x.RankVotingId,
                        principalTable: "RankVotings",
                        principalColumn: "ScoreId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteMaps_LeaderboardId",
                table: "FavoriteMaps",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteMaps_PlayerId",
                table: "FavoriteMaps",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteMaps_RankVotingId",
                table: "FavoriteMaps",
                column: "RankVotingId",
                unique: true,
                filter: "[RankVotingId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FavoriteMaps");

            migrationBuilder.DropColumn(
                name: "Speed",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "LeaderboardId",
                table: "RankVotings");

            migrationBuilder.DropColumn(
                name: "FansCount",
                table: "Leaderboards");
        }
    }
}
