using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class PatreonFeatures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PatreonFeaturesId",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PatreonFeatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Bio = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeftSaberColor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RightSaberColor = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatreonFeatures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Players_PatreonFeaturesId",
                table: "Players",
                column: "PatreonFeaturesId");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_PatreonFeatures_PatreonFeaturesId",
                table: "Players",
                column: "PatreonFeaturesId",
                principalTable: "PatreonFeatures",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_PatreonFeatures_PatreonFeaturesId",
                table: "Players");

            migrationBuilder.DropTable(
                name: "PatreonFeatures");

            migrationBuilder.DropIndex(
                name: "IX_Players_PatreonFeaturesId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PatreonFeaturesId",
                table: "Players");
        }
    }
}
