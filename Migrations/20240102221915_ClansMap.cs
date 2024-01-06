using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ClansMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CapturedTime",
                table: "Leaderboards",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "GlobalMapX",
                table: "Clans",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "GlobalMapY",
                table: "Clans",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.CreateTable(
                name: "GlobalMapHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timestamp = table.Column<int>(type: "int", nullable: false),
                    ClanId = table.Column<int>(type: "int", nullable: false),
                    GlobalMapCaptured = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalMapHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlobalMapHistory_Clans_ClanId",
                        column: x => x.ClanId,
                        principalTable: "Clans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalMapHistory_ClanId",
                table: "GlobalMapHistory",
                column: "ClanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlobalMapHistory");

            migrationBuilder.DropColumn(
                name: "CapturedTime",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "GlobalMapX",
                table: "Clans");

            migrationBuilder.DropColumn(
                name: "GlobalMapY",
                table: "Clans");
        }
    }
}
