using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class FriendsAndClans : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomAvatar",
                table: "Users");

            migrationBuilder.AddColumn<int>(
                name: "TopHMD",
                table: "Stats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TopPlatform",
                table: "Stats",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "Scores",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PlayerFriendsId",
                table: "Players",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Clans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeaderID = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlayersCount = table.Column<int>(type: "int", nullable: false),
                    Pp = table.Column<int>(type: "int", nullable: false),
                    AverageRank = table.Column<int>(type: "int", nullable: false),
                    AverageAccuracy = table.Column<int>(type: "int", nullable: false),
                    BannedClanId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ClanRequestId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clans_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Clans_Users_BannedClanId",
                        column: x => x.BannedClanId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Clans_Users_ClanRequestId",
                        column: x => x.ClanRequestId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Friends",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friends", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScoreRemovalLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Replay = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdminId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreRemovalLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Players_PlayerFriendsId",
                table: "Players",
                column: "PlayerFriendsId");

            migrationBuilder.CreateIndex(
                name: "IX_Clans_BannedClanId",
                table: "Clans",
                column: "BannedClanId");

            migrationBuilder.CreateIndex(
                name: "IX_Clans_ClanRequestId",
                table: "Clans",
                column: "ClanRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Clans_PlayerId",
                table: "Clans",
                column: "PlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Friends_PlayerFriendsId",
                table: "Players",
                column: "PlayerFriendsId",
                principalTable: "Friends",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_Friends_PlayerFriendsId",
                table: "Players");

            migrationBuilder.DropTable(
                name: "Clans");

            migrationBuilder.DropTable(
                name: "Friends");

            migrationBuilder.DropTable(
                name: "ScoreRemovalLogs");

            migrationBuilder.DropIndex(
                name: "IX_Players_PlayerFriendsId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "TopHMD",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "TopPlatform",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "PlayerFriendsId",
                table: "Players");

            migrationBuilder.AddColumn<bool>(
                name: "CustomAvatar",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
