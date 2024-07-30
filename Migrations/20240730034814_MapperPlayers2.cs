using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class MapperPlayers2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Players_MapperId",
                table: "Players",
                column: "MapperId",
                unique: true,
                filter: "[MapperId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Mappers_MapperId",
                table: "Players",
                column: "MapperId",
                principalTable: "Mappers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_Mappers_MapperId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Players_MapperId",
                table: "Players");
        }
    }
}
