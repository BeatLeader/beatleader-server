using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class MOTDEventsPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VideoPreviewUrl",
                table: "Songs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MapOfTheDayPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Points = table.Column<int>(type: "int", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    MapOfTheDayId = table.Column<int>(type: "int", nullable: false),
                    EventPlayerId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapOfTheDayPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MapOfTheDayPoints_EventPlayer_EventPlayerId",
                        column: x => x.EventPlayerId,
                        principalTable: "EventPlayer",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MapOfTheDayPoints_MapOfTheDay_MapOfTheDayId",
                        column: x => x.MapOfTheDayId,
                        principalTable: "MapOfTheDay",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MapOfTheDayPoints_EventPlayerId",
                table: "MapOfTheDayPoints",
                column: "EventPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MapOfTheDayPoints_MapOfTheDayId",
                table: "MapOfTheDayPoints",
                column: "MapOfTheDayId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapOfTheDayPoints");

            migrationBuilder.DropColumn(
                name: "VideoPreviewUrl",
                table: "Songs");
        }
    }
}
