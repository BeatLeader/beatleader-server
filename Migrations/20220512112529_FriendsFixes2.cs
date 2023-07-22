using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class FriendsFixes2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerPlayerFriends",
                columns: table => new
                {
                    FriendsId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PlayerFriendsId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerPlayerFriends", x => new { x.FriendsId, x.PlayerFriendsId });
                    table.ForeignKey(
                        name: "FK_PlayerPlayerFriends_Friends_PlayerFriendsId",
                        column: x => x.PlayerFriendsId,
                        principalTable: "Friends",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerPlayerFriends_Players_FriendsId",
                        column: x => x.FriendsId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerPlayerFriends_PlayerFriendsId",
                table: "PlayerPlayerFriends",
                column: "PlayerFriendsId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerPlayerFriends");
        }
    }
}
