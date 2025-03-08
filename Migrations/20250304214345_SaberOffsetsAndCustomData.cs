using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class SaberOffsetsAndCustomData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedTime",
                table: "Songs");

            migrationBuilder.AddColumn<int>(
                name: "CustomData",
                table: "ReplayOffsets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SaberOffsets",
                table: "ReplayOffsets",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomData",
                table: "ReplayOffsets");

            migrationBuilder.DropColumn(
                name: "SaberOffsets",
                table: "ReplayOffsets");

            migrationBuilder.AddColumn<string>(
                name: "CreatedTime",
                table: "Songs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
