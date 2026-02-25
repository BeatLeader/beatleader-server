using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class MapOffsetAndCustomDiffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomDifficultyName",
                table: "DifficultyDescription",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NoteJumpStartBeatOffset",
                table: "DifficultyDescription",
                type: "float",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomDifficultyName",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "NoteJumpStartBeatOffset",
                table: "DifficultyDescription");
        }
    }
}
