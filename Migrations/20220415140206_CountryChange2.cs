using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class CountryChange2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.CreateTable(
                name: "CountryChanges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Timestamp = table.Column<int>(type: "int", nullable: false),
                    OldCountry = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewCountry = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryChanges", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CountryChanges");

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
    }
}
