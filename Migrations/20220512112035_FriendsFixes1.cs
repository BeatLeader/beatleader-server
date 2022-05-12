using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class FriendsFixes1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_Friends_PlayerFriendsId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Players_PlayerFriendsId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PlayerFriendsId",
                table: "Players");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlayerFriendsId",
                table: "Players",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_PlayerFriendsId",
                table: "Players",
                column: "PlayerFriendsId");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Friends_PlayerFriendsId",
                table: "Players",
                column: "PlayerFriendsId",
                principalTable: "Friends",
                principalColumn: "Id");
        }
    }
}
