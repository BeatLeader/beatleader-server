using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class PlayerChanges2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerChange");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerChange",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NewCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Timestamp = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerChange", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerChange_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerChange_PlayerId",
                table: "PlayerChange",
                column: "PlayerId");
        }
    }
}
