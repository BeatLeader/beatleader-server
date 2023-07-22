using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class LeaderboardChanges2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankChanges");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RankChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NewModifiersModifierId = table.Column<int>(type: "int", nullable: true),
                    OldModifiersModifierId = table.Column<int>(type: "int", nullable: true),
                    Diff = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewCriteriaMet = table.Column<int>(type: "int", nullable: false),
                    NewRankability = table.Column<float>(type: "real", nullable: false),
                    NewStars = table.Column<float>(type: "real", nullable: false),
                    NewType = table.Column<int>(type: "int", nullable: false),
                    OldCriteriaMet = table.Column<int>(type: "int", nullable: false),
                    OldRankability = table.Column<float>(type: "real", nullable: false),
                    OldStars = table.Column<float>(type: "real", nullable: false),
                    OldType = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RankChanges_Modifiers_NewModifiersModifierId",
                        column: x => x.NewModifiersModifierId,
                        principalTable: "Modifiers",
                        principalColumn: "ModifierId");
                    table.ForeignKey(
                        name: "FK_RankChanges_Modifiers_OldModifiersModifierId",
                        column: x => x.OldModifiersModifierId,
                        principalTable: "Modifiers",
                        principalColumn: "ModifierId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankChanges_NewModifiersModifierId",
                table: "RankChanges",
                column: "NewModifiersModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_RankChanges_OldModifiersModifierId",
                table: "RankChanges",
                column: "OldModifiersModifierId");
        }
    }
}
