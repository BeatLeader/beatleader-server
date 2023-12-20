using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ReePresets3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReeSabersComment_Players_AuthorId",
                table: "ReeSabersComment");

            migrationBuilder.RenameColumn(
                name: "Message",
                table: "ReeSabersComment",
                newName: "Value");

            migrationBuilder.RenameColumn(
                name: "AuthorId",
                table: "ReeSabersComment",
                newName: "PlayerId");

            migrationBuilder.RenameIndex(
                name: "IX_ReeSabersComment_AuthorId",
                table: "ReeSabersComment",
                newName: "IX_ReeSabersComment_PlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReeSabersComment_Players_PlayerId",
                table: "ReeSabersComment",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReeSabersComment_Players_PlayerId",
                table: "ReeSabersComment");

            migrationBuilder.RenameColumn(
                name: "Value",
                table: "ReeSabersComment",
                newName: "Message");

            migrationBuilder.RenameColumn(
                name: "PlayerId",
                table: "ReeSabersComment",
                newName: "AuthorId");

            migrationBuilder.RenameIndex(
                name: "IX_ReeSabersComment_PlayerId",
                table: "ReeSabersComment",
                newName: "IX_ReeSabersComment_AuthorId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReeSabersComment_Players_AuthorId",
                table: "ReeSabersComment",
                column: "AuthorId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
