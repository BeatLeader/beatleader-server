using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class MainClan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TopClanId",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_TopClanId",
                table: "Players",
                column: "TopClanId");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Clans_TopClanId",
                table: "Players",
                column: "TopClanId",
                principalTable: "Clans",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_Clans_TopClanId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Players_TopClanId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "TopClanId",
                table: "Players");
        }
    }
}
