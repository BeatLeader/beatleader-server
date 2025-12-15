using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class PrestigeLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrestiegeLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Level = table.Column<int>(type: "int", nullable: false),
                    BigIcon = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SmallIcon = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrestiegeLevels", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrestiegeLevels");
        }
    }
}
