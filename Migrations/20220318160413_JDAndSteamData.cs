using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class JDAndSteamData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "JumpDistance",
                table: "WinTracker",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "AllTime",
                table: "Players",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<string>(
                name: "ExternalProfileUrl",
                table: "Players",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<float>(
                name: "LastTwoWeeksTime",
                table: "Players",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JumpDistance",
                table: "WinTracker");

            migrationBuilder.DropColumn(
                name: "AllTime",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ExternalProfileUrl",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LastTwoWeeksTime",
                table: "Players");
        }
    }
}
