using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class ClanMoreInfo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UserInfo",
                table: "Clans",
                newName: "Description");

            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "Clans",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bio",
                table: "Clans");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Clans",
                newName: "UserInfo");
        }
    }
}
