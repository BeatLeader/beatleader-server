using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class YoinkOPA1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Scores_ReplayIdentification_IdentificationId",
                table: "Scores");

            migrationBuilder.DropTable(
                name: "ReplayIdentification");

            migrationBuilder.DropIndex(
                name: "IX_Scores_IdentificationId",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "IdentificationId",
                table: "Scores");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdentificationId",
                table: "Scores",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "ReplayIdentification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Value = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplayIdentification", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_IdentificationId",
                table: "Scores",
                column: "IdentificationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Scores_ReplayIdentification_IdentificationId",
                table: "Scores",
                column: "IdentificationId",
                principalTable: "ReplayIdentification",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
