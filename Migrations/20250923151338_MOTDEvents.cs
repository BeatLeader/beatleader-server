using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class MOTDEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MapOfTheDayId",
                table: "Leaderboards",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EventType",
                table: "EventRankings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MapOfTheDay",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SongId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Timestart = table.Column<int>(type: "int", nullable: false),
                    Timeend = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EventRankingId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapOfTheDay", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MapOfTheDay_EventRankings_EventRankingId",
                        column: x => x.EventRankingId,
                        principalTable: "EventRankings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MapOfTheDay_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leaderboards_MapOfTheDayId",
                table: "Leaderboards",
                column: "MapOfTheDayId");

            migrationBuilder.CreateIndex(
                name: "IX_MapOfTheDay_EventRankingId",
                table: "MapOfTheDay",
                column: "EventRankingId");

            migrationBuilder.CreateIndex(
                name: "IX_MapOfTheDay_SongId",
                table: "MapOfTheDay",
                column: "SongId");

            migrationBuilder.AddForeignKey(
                name: "FK_Leaderboards_MapOfTheDay_MapOfTheDayId",
                table: "Leaderboards",
                column: "MapOfTheDayId",
                principalTable: "MapOfTheDay",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leaderboards_MapOfTheDay_MapOfTheDayId",
                table: "Leaderboards");

            migrationBuilder.DropTable(
                name: "MapOfTheDay");

            migrationBuilder.DropIndex(
                name: "IX_Leaderboards_MapOfTheDayId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "MapOfTheDayId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "EventType",
                table: "EventRankings");
        }
    }
}
