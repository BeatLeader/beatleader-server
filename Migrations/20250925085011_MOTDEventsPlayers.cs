using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class MOTDEventsPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
        }
    }
}
