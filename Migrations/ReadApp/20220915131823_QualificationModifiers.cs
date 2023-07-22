using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class QualificationModifiers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ModifiersModifierId",
                table: "RankQualification",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NewModifiersModifierId",
                table: "QualificationChange",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OldModifiersModifierId",
                table: "QualificationChange",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RankQualification_ModifiersModifierId",
                table: "RankQualification",
                column: "ModifiersModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_QualificationChange_NewModifiersModifierId",
                table: "QualificationChange",
                column: "NewModifiersModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_QualificationChange_OldModifiersModifierId",
                table: "QualificationChange",
                column: "OldModifiersModifierId");

            migrationBuilder.AddForeignKey(
                name: "FK_QualificationChange_Modifiers_NewModifiersModifierId",
                table: "QualificationChange",
                column: "NewModifiersModifierId",
                principalTable: "Modifiers",
                principalColumn: "ModifierId");

            migrationBuilder.AddForeignKey(
                name: "FK_QualificationChange_Modifiers_OldModifiersModifierId",
                table: "QualificationChange",
                column: "OldModifiersModifierId",
                principalTable: "Modifiers",
                principalColumn: "ModifierId");

            migrationBuilder.AddForeignKey(
                name: "FK_RankQualification_Modifiers_ModifiersModifierId",
                table: "RankQualification",
                column: "ModifiersModifierId",
                principalTable: "Modifiers",
                principalColumn: "ModifierId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QualificationChange_Modifiers_NewModifiersModifierId",
                table: "QualificationChange");

            migrationBuilder.DropForeignKey(
                name: "FK_QualificationChange_Modifiers_OldModifiersModifierId",
                table: "QualificationChange");

            migrationBuilder.DropForeignKey(
                name: "FK_RankQualification_Modifiers_ModifiersModifierId",
                table: "RankQualification");

            migrationBuilder.DropIndex(
                name: "IX_RankQualification_ModifiersModifierId",
                table: "RankQualification");

            migrationBuilder.DropIndex(
                name: "IX_QualificationChange_NewModifiersModifierId",
                table: "QualificationChange");

            migrationBuilder.DropIndex(
                name: "IX_QualificationChange_OldModifiersModifierId",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "ModifiersModifierId",
                table: "RankQualification");

            migrationBuilder.DropColumn(
                name: "NewModifiersModifierId",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "OldModifiersModifierId",
                table: "QualificationChange");
        }
    }
}
