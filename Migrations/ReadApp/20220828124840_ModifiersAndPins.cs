using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class ModifiersAndPins : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AverageHeight",
                table: "WinTracker",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "PeakRank",
                table: "Stats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "MetadataId",
                table: "Scores",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModifierValuesModifierId",
                table: "DifficultyDescription",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Modifiers",
                columns: table => new
                {
                    ModifierId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DA = table.Column<float>(type: "real", nullable: false),
                    FS = table.Column<float>(type: "real", nullable: false),
                    SS = table.Column<float>(type: "real", nullable: false),
                    SF = table.Column<float>(type: "real", nullable: false),
                    GN = table.Column<float>(type: "real", nullable: false),
                    NA = table.Column<float>(type: "real", nullable: false),
                    NB = table.Column<float>(type: "real", nullable: false),
                    NF = table.Column<float>(type: "real", nullable: false),
                    NO = table.Column<float>(type: "real", nullable: false),
                    PM = table.Column<float>(type: "real", nullable: false),
                    SC = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modifiers", x => x.ModifierId);
                });

            migrationBuilder.CreateTable(
                name: "ScoreMetadata",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkService = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkServiceIcon = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Link = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreMetadata", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_MetadataId",
                table: "Scores",
                column: "MetadataId");

            migrationBuilder.CreateIndex(
                name: "IX_DifficultyDescription_ModifierValuesModifierId",
                table: "DifficultyDescription",
                column: "ModifierValuesModifierId");

            migrationBuilder.AddForeignKey(
                name: "FK_DifficultyDescription_Modifiers_ModifierValuesModifierId",
                table: "DifficultyDescription",
                column: "ModifierValuesModifierId",
                principalTable: "Modifiers",
                principalColumn: "ModifierId");

            migrationBuilder.AddForeignKey(
                name: "FK_Scores_ScoreMetadata_MetadataId",
                table: "Scores",
                column: "MetadataId",
                principalTable: "ScoreMetadata",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DifficultyDescription_Modifiers_ModifierValuesModifierId",
                table: "DifficultyDescription");

            migrationBuilder.DropForeignKey(
                name: "FK_Scores_ScoreMetadata_MetadataId",
                table: "Scores");

            migrationBuilder.DropTable(
                name: "Modifiers");

            migrationBuilder.DropTable(
                name: "ScoreMetadata");

            migrationBuilder.DropIndex(
                name: "IX_Scores_MetadataId",
                table: "Scores");

            migrationBuilder.DropIndex(
                name: "IX_DifficultyDescription_ModifierValuesModifierId",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "AverageHeight",
                table: "WinTracker");

            migrationBuilder.DropColumn(
                name: "PeakRank",
                table: "Stats");

            migrationBuilder.DropColumn(
                name: "MetadataId",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "ModifierValuesModifierId",
                table: "DifficultyDescription");
        }
    }
}
