using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class PlayerChanges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReplayOffsetsId",
                table: "Scores",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlayerChange",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Timestamp = table.Column<int>(type: "int", nullable: false),
                    OldName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerChange", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerChange_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReplayOffsets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Frames = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<int>(type: "int", nullable: false),
                    Walls = table.Column<int>(type: "int", nullable: false),
                    Heights = table.Column<int>(type: "int", nullable: false),
                    Pauses = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplayOffsets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_ReplayOffsetsId",
                table: "Scores",
                column: "ReplayOffsetsId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerChange_PlayerId",
                table: "PlayerChange",
                column: "PlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Scores_ReplayOffsets_ReplayOffsetsId",
                table: "Scores",
                column: "ReplayOffsetsId",
                principalTable: "ReplayOffsets",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Scores_ReplayOffsets_ReplayOffsetsId",
                table: "Scores");

            migrationBuilder.DropTable(
                name: "PlayerChange");

            migrationBuilder.DropTable(
                name: "ReplayOffsets");

            migrationBuilder.DropIndex(
                name: "IX_Scores_ReplayOffsetsId",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "ReplayOffsetsId",
                table: "Scores");
        }
    }
}
