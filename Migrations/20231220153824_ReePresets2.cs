using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ReePresets2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CommentsDisabled",
                table: "ReeSabersPresets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EditTimeset",
                table: "ReeSabersComment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Edited",
                table: "ReeSabersComment",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommentsDisabled",
                table: "ReeSabersPresets");

            migrationBuilder.DropColumn(
                name: "EditTimeset",
                table: "ReeSabersComment");

            migrationBuilder.DropColumn(
                name: "Edited",
                table: "ReeSabersComment");
        }
    }
}
