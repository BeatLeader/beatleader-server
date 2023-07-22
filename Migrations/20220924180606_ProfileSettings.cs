using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class ProfileSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProfileSettingsId",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProfileSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Bio = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EffectName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProfileAppearance = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Hue = table.Column<float>(type: "real", nullable: true),
                    Saturation = table.Column<float>(type: "real", nullable: true),
                    LeftSaberColor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RightSaberColor = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Players_ProfileSettingsId",
                table: "Players",
                column: "ProfileSettingsId");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_ProfileSettings_ProfileSettingsId",
                table: "Players",
                column: "ProfileSettingsId",
                principalTable: "ProfileSettings",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_ProfileSettings_ProfileSettingsId",
                table: "Players");

            migrationBuilder.DropTable(
                name: "ProfileSettings");

            migrationBuilder.DropIndex(
                name: "IX_Players_ProfileSettingsId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ProfileSettingsId",
                table: "Players");
        }
    }
}
