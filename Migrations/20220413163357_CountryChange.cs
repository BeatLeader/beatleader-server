using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class CountryChange : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AuthIDs",
                table: "AuthIDs");

            migrationBuilder.RenameTable(
                name: "AuthIDs",
                newName: "AuthID");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AuthID",
                table: "AuthID",
                column: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AuthID",
                table: "AuthID");

            migrationBuilder.RenameTable(
                name: "AuthID",
                newName: "AuthIDs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AuthIDs",
                table: "AuthIDs",
                column: "Id");
        }
    }
}
