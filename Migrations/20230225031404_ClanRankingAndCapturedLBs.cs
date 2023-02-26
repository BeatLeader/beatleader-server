using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ClanRankingAndCapturedLBs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClanId",
                table: "Leaderboards",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClanRanking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClanId = table.Column<int>(type: "int", nullable: true),
                    ClanPP = table.Column<float>(type: "real", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanRanking", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClanRanking_Clans_ClanId",
                        column: x => x.ClanId,
                        principalTable: "Clans",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClanRanking_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leaderboards_Clans_ClanId",
                table: "Leaderboards");

            migrationBuilder.DropTable(
                name: "ClanRanking");

            migrationBuilder.DropIndex(
                name: "IX_Leaderboards_ClanId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "ClanId",
                table: "Leaderboards");
        }
    }
}
