using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class DeveloperProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeveloperProfileId",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeveloperProfileId",
                table: "OpenIddictApplications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeveloperProfile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperProfile", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Players_DeveloperProfileId",
                table: "Players",
                column: "DeveloperProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictApplications_DeveloperProfileId",
                table: "OpenIddictApplications",
                column: "DeveloperProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_OpenIddictApplications_DeveloperProfile_DeveloperProfileId",
                table: "OpenIddictApplications",
                column: "DeveloperProfileId",
                principalTable: "DeveloperProfile",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_DeveloperProfile_DeveloperProfileId",
                table: "Players",
                column: "DeveloperProfileId",
                principalTable: "DeveloperProfile",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OpenIddictApplications_DeveloperProfile_DeveloperProfileId",
                table: "OpenIddictApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_Players_DeveloperProfile_DeveloperProfileId",
                table: "Players");

            migrationBuilder.DropTable(
                name: "DeveloperProfile");

            migrationBuilder.DropIndex(
                name: "IX_Players_DeveloperProfileId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_OpenIddictApplications_DeveloperProfileId",
                table: "OpenIddictApplications");

            migrationBuilder.DropColumn(
                name: "DeveloperProfileId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "DeveloperProfileId",
                table: "OpenIddictApplications");
        }
    }
}
