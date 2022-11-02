using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class LastWeekStats2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_Stats_ScoreStatsId",
                table: "Players");

            migrationBuilder.AlterColumn<int>(
                name: "ScoreStatsId",
                table: "Players",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Stats_ScoreStatsId",
                table: "Players",
                column: "ScoreStatsId",
                principalTable: "Stats",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_Stats_ScoreStatsId",
                table: "Players");

            migrationBuilder.AlterColumn<int>(
                name: "ScoreStatsId",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Stats_ScoreStatsId",
                table: "Players",
                column: "ScoreStatsId",
                principalTable: "Stats",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
