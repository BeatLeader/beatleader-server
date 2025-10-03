using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class MOTDEventsPlayers2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EventPlayer_MapOfTheDay_MapOfTheDayId",
                table: "EventPlayer");

            migrationBuilder.DropIndex(
                name: "IX_EventPlayer_MapOfTheDayId",
                table: "EventPlayer");

            migrationBuilder.DropColumn(
                name: "MapOfTheDayId",
                table: "EventPlayer");

            migrationBuilder.CreateTable(
                name: "EventPlayerMapOfTheDay",
                columns: table => new
                {
                    ChampionsId = table.Column<int>(type: "int", nullable: false),
                    MapOfTheDaysId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventPlayerMapOfTheDay", x => new { x.ChampionsId, x.MapOfTheDaysId });
                    table.ForeignKey(
                        name: "FK_EventPlayerMapOfTheDay_EventPlayer_ChampionsId",
                        column: x => x.ChampionsId,
                        principalTable: "EventPlayer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventPlayerMapOfTheDay_MapOfTheDay_MapOfTheDaysId",
                        column: x => x.MapOfTheDaysId,
                        principalTable: "MapOfTheDay",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventPlayerMapOfTheDay_MapOfTheDaysId",
                table: "EventPlayerMapOfTheDay",
                column: "MapOfTheDaysId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventPlayerMapOfTheDay");

            migrationBuilder.AddColumn<int>(
                name: "MapOfTheDayId",
                table: "EventPlayer",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventPlayer_MapOfTheDayId",
                table: "EventPlayer",
                column: "MapOfTheDayId");

            migrationBuilder.AddForeignKey(
                name: "FK_EventPlayer_MapOfTheDay_MapOfTheDayId",
                table: "EventPlayer",
                column: "MapOfTheDayId",
                principalTable: "MapOfTheDay",
                principalColumn: "Id");
        }
    }
}
