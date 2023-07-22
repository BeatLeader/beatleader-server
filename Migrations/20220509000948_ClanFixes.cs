using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class ClanFixes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clans_Players_PlayerId",
                table: "Clans");

            migrationBuilder.DropForeignKey(
                name: "FK_Clans_Users_BannedClanId",
                table: "Clans");

            migrationBuilder.DropForeignKey(
                name: "FK_Clans_Users_ClanRequestId",
                table: "Clans");

            migrationBuilder.DropIndex(
                name: "IX_Clans_BannedClanId",
                table: "Clans");

            migrationBuilder.DropIndex(
                name: "IX_Clans_ClanRequestId",
                table: "Clans");

            migrationBuilder.DropIndex(
                name: "IX_Clans_PlayerId",
                table: "Clans");

            migrationBuilder.DropColumn(
                name: "BannedClanId",
                table: "Clans");

            migrationBuilder.DropColumn(
                name: "ClanRequestId",
                table: "Clans");

            migrationBuilder.DropColumn(
                name: "PlayerId",
                table: "Clans");

            migrationBuilder.AlterColumn<float>(
                name: "Pp",
                table: "Clans",
                type: "real",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<float>(
                name: "AverageRank",
                table: "Clans",
                type: "real",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<float>(
                name: "AverageAccuracy",
                table: "Clans",
                type: "real",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "UserInfo",
                table: "Clans",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ClanPlayer",
                columns: table => new
                {
                    ClansId = table.Column<int>(type: "int", nullable: false),
                    PlayersId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanPlayer", x => new { x.ClansId, x.PlayersId });
                    table.ForeignKey(
                        name: "FK_ClanPlayer_Clans_ClansId",
                        column: x => x.ClansId,
                        principalTable: "Clans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClanPlayer_Players_PlayersId",
                        column: x => x.PlayersId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClanUser",
                columns: table => new
                {
                    BannedClansId = table.Column<int>(type: "int", nullable: false),
                    BannedId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanUser", x => new { x.BannedClansId, x.BannedId });
                    table.ForeignKey(
                        name: "FK_ClanUser_Clans_BannedClansId",
                        column: x => x.BannedClansId,
                        principalTable: "Clans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClanUser_Users_BannedId",
                        column: x => x.BannedId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClanUser1",
                columns: table => new
                {
                    ClanRequestId = table.Column<int>(type: "int", nullable: false),
                    RequestsId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanUser1", x => new { x.ClanRequestId, x.RequestsId });
                    table.ForeignKey(
                        name: "FK_ClanUser1_Clans_ClanRequestId",
                        column: x => x.ClanRequestId,
                        principalTable: "Clans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClanUser1_Users_RequestsId",
                        column: x => x.RequestsId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClanPlayer_PlayersId",
                table: "ClanPlayer",
                column: "PlayersId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanUser_BannedId",
                table: "ClanUser",
                column: "BannedId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanUser1_RequestsId",
                table: "ClanUser1",
                column: "RequestsId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClanPlayer");

            migrationBuilder.DropTable(
                name: "ClanUser");

            migrationBuilder.DropTable(
                name: "ClanUser1");

            migrationBuilder.DropColumn(
                name: "UserInfo",
                table: "Clans");

            migrationBuilder.AlterColumn<int>(
                name: "Pp",
                table: "Clans",
                type: "int",
                nullable: false,
                oldClrType: typeof(float),
                oldType: "real");

            migrationBuilder.AlterColumn<int>(
                name: "AverageRank",
                table: "Clans",
                type: "int",
                nullable: false,
                oldClrType: typeof(float),
                oldType: "real");

            migrationBuilder.AlterColumn<int>(
                name: "AverageAccuracy",
                table: "Clans",
                type: "int",
                nullable: false,
                oldClrType: typeof(float),
                oldType: "real");

            migrationBuilder.AddColumn<string>(
                name: "BannedClanId",
                table: "Clans",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClanRequestId",
                table: "Clans",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerId",
                table: "Clans",
                type: "nvarchar(450)",
                nullable: true);

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
                name: "FK_Clans_Players_PlayerId",
                table: "Clans",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Clans_Users_BannedClanId",
                table: "Clans",
                column: "BannedClanId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Clans_Users_ClanRequestId",
                table: "Clans",
                column: "ClanRequestId",
                principalTable: "Users",
                principalColumn: "Id");
        }
    }
}
