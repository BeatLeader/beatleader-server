using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class MOTDEventsFeatured : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnimatedImage",
                table: "EventRankings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FeaturedPlaylistId",
                table: "EventRankings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventRankings_FeaturedPlaylistId",
                table: "EventRankings",
                column: "FeaturedPlaylistId");

            migrationBuilder.AddForeignKey(
                name: "FK_EventRankings_FeaturedPlaylist_FeaturedPlaylistId",
                table: "EventRankings",
                column: "FeaturedPlaylistId",
                principalTable: "FeaturedPlaylist",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EventRankings_FeaturedPlaylist_FeaturedPlaylistId",
                table: "EventRankings");

            migrationBuilder.DropIndex(
                name: "IX_EventRankings_FeaturedPlaylistId",
                table: "EventRankings");

            migrationBuilder.DropColumn(
                name: "AnimatedImage",
                table: "EventRankings");

            migrationBuilder.DropColumn(
                name: "FeaturedPlaylistId",
                table: "EventRankings");
        }
    }
}
