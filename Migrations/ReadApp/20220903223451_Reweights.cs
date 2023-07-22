using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class Reweights : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NewCriteriaMet",
                table: "RankChanges",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NewModifiersModifierId",
                table: "RankChanges",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OldCriteriaMet",
                table: "RankChanges",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OldModifiersModifierId",
                table: "RankChanges",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReweightId",
                table: "Leaderboards",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RankUpdate",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    RTMember = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Keep = table.Column<bool>(type: "bit", nullable: false),
                    Stars = table.Column<float>(type: "real", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    CriteriaMet = table.Column<int>(type: "int", nullable: false),
                    CriteriaCommentary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Finished = table.Column<bool>(type: "bit", nullable: false),
                    ModifiersModifierId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankUpdate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RankUpdate_Modifiers_ModifiersModifierId",
                        column: x => x.ModifiersModifierId,
                        principalTable: "Modifiers",
                        principalColumn: "ModifierId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RankUpdateChange",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldKeep = table.Column<bool>(type: "bit", nullable: false),
                    OldStars = table.Column<float>(type: "real", nullable: false),
                    OldType = table.Column<int>(type: "int", nullable: false),
                    OldCriteriaMet = table.Column<int>(type: "int", nullable: false),
                    OldCriteriaCommentary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldModifiersModifierId = table.Column<int>(type: "int", nullable: true),
                    NewKeep = table.Column<bool>(type: "bit", nullable: false),
                    NewStars = table.Column<float>(type: "real", nullable: false),
                    NewType = table.Column<int>(type: "int", nullable: false),
                    NewCriteriaMet = table.Column<int>(type: "int", nullable: false),
                    NewCriteriaCommentary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewModifiersModifierId = table.Column<int>(type: "int", nullable: true),
                    RankUpdateId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankUpdateChange", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RankUpdateChange_Modifiers_NewModifiersModifierId",
                        column: x => x.NewModifiersModifierId,
                        principalTable: "Modifiers",
                        principalColumn: "ModifierId");
                    table.ForeignKey(
                        name: "FK_RankUpdateChange_Modifiers_OldModifiersModifierId",
                        column: x => x.OldModifiersModifierId,
                        principalTable: "Modifiers",
                        principalColumn: "ModifierId");
                    table.ForeignKey(
                        name: "FK_RankUpdateChange_RankUpdate_RankUpdateId",
                        column: x => x.RankUpdateId,
                        principalTable: "RankUpdate",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankChanges_NewModifiersModifierId",
                table: "RankChanges",
                column: "NewModifiersModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_RankChanges_OldModifiersModifierId",
                table: "RankChanges",
                column: "OldModifiersModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_Leaderboards_ReweightId",
                table: "Leaderboards",
                column: "ReweightId");

            migrationBuilder.CreateIndex(
                name: "IX_RankUpdate_ModifiersModifierId",
                table: "RankUpdate",
                column: "ModifiersModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_RankUpdateChange_NewModifiersModifierId",
                table: "RankUpdateChange",
                column: "NewModifiersModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_RankUpdateChange_OldModifiersModifierId",
                table: "RankUpdateChange",
                column: "OldModifiersModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_RankUpdateChange_RankUpdateId",
                table: "RankUpdateChange",
                column: "RankUpdateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Leaderboards_RankUpdate_ReweightId",
                table: "Leaderboards",
                column: "ReweightId",
                principalTable: "RankUpdate",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RankChanges_Modifiers_NewModifiersModifierId",
                table: "RankChanges",
                column: "NewModifiersModifierId",
                principalTable: "Modifiers",
                principalColumn: "ModifierId");

            migrationBuilder.AddForeignKey(
                name: "FK_RankChanges_Modifiers_OldModifiersModifierId",
                table: "RankChanges",
                column: "OldModifiersModifierId",
                principalTable: "Modifiers",
                principalColumn: "ModifierId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leaderboards_RankUpdate_ReweightId",
                table: "Leaderboards");

            migrationBuilder.DropForeignKey(
                name: "FK_RankChanges_Modifiers_NewModifiersModifierId",
                table: "RankChanges");

            migrationBuilder.DropForeignKey(
                name: "FK_RankChanges_Modifiers_OldModifiersModifierId",
                table: "RankChanges");

            migrationBuilder.DropTable(
                name: "RankUpdateChange");

            migrationBuilder.DropTable(
                name: "RankUpdate");

            migrationBuilder.DropIndex(
                name: "IX_RankChanges_NewModifiersModifierId",
                table: "RankChanges");

            migrationBuilder.DropIndex(
                name: "IX_RankChanges_OldModifiersModifierId",
                table: "RankChanges");

            migrationBuilder.DropIndex(
                name: "IX_Leaderboards_ReweightId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "NewCriteriaMet",
                table: "RankChanges");

            migrationBuilder.DropColumn(
                name: "NewModifiersModifierId",
                table: "RankChanges");

            migrationBuilder.DropColumn(
                name: "OldCriteriaMet",
                table: "RankChanges");

            migrationBuilder.DropColumn(
                name: "OldModifiersModifierId",
                table: "RankChanges");

            migrationBuilder.DropColumn(
                name: "ReweightId",
                table: "Leaderboards");
        }
    }
}
