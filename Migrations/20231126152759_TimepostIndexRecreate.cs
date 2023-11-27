using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class TimepostIndexRecreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_Timepost",
                table: "Scores");

            migrationBuilder.AlterColumn<string>(
                name: "Replay",
                table: "Scores",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Scores_Timepost_Replay",
                table: "Scores",
                columns: new[] { "Timepost", "Replay" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_Timepost_Replay",
                table: "Scores");

            migrationBuilder.AlterColumn<string>(
                name: "Replay",
                table: "Scores",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Scores_Timepost",
                table: "Scores",
                column: "Timepost");
        }
    }
}
