using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class LeaderboardChanges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaderboardChange",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldRankability = table.Column<float>(type: "real", nullable: false),
                    OldStars = table.Column<float>(type: "real", nullable: false),
                    OldType = table.Column<int>(type: "int", nullable: false),
                    OldCriteriaMet = table.Column<int>(type: "int", nullable: false),
                    OldModifiersModifierId = table.Column<int>(type: "int", nullable: true),
                    NewRankability = table.Column<float>(type: "real", nullable: false),
                    NewStars = table.Column<float>(type: "real", nullable: false),
                    NewType = table.Column<int>(type: "int", nullable: false),
                    NewCriteriaMet = table.Column<int>(type: "int", nullable: false),
                    NewModifiersModifierId = table.Column<int>(type: "int", nullable: true),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardChange", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaderboardChange_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaderboardChange_Modifiers_NewModifiersModifierId",
                        column: x => x.NewModifiersModifierId,
                        principalTable: "Modifiers",
                        principalColumn: "ModifierId");
                    table.ForeignKey(
                        name: "FK_LeaderboardChange_Modifiers_OldModifiersModifierId",
                        column: x => x.OldModifiersModifierId,
                        principalTable: "Modifiers",
                        principalColumn: "ModifierId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardChange_LeaderboardId",
                table: "LeaderboardChange",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardChange_NewModifiersModifierId",
                table: "LeaderboardChange",
                column: "NewModifiersModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardChange_OldModifiersModifierId",
                table: "LeaderboardChange",
                column: "OldModifiersModifierId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaderboardChange");
        }
    }
}
