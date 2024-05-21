using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class PublicFollowersAliasesAndModders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Alias",
                table: "Players",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedAt",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OldAlias",
                table: "Players",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HideFriends",
                table: "Friends",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ModDescription",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Cover = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GithubLink = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Downloads = table.Column<int>(type: "int", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    DeveloperProfileId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModDescription", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModDescription_DeveloperProfile_DeveloperProfileId",
                        column: x => x.DeveloperProfileId,
                        principalTable: "DeveloperProfile",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModDescription_DeveloperProfileId",
                table: "ModDescription",
                column: "DeveloperProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModDescription");

            migrationBuilder.DropColumn(
                name: "Alias",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "OldAlias",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "HideFriends",
                table: "Friends");
        }
    }
}
