using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class Tree2024 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TreeMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BundleId = table.Column<int>(type: "int", nullable: false),
                    SongId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestart = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreeMaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TreeOrnaments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BundleId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreeOrnaments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerTreeOrnaments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrnamentId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ScoreId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerTreeOrnaments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerTreeOrnaments_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerTreeOrnaments_Scores_ScoreId",
                        column: x => x.ScoreId,
                        principalTable: "Scores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayerTreeOrnaments_TreeOrnaments_OrnamentId",
                        column: x => x.OrnamentId,
                        principalTable: "TreeOrnaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTreeOrnaments_OrnamentId",
                table: "PlayerTreeOrnaments",
                column: "OrnamentId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTreeOrnaments_PlayerId",
                table: "PlayerTreeOrnaments",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTreeOrnaments_ScoreId",
                table: "PlayerTreeOrnaments",
                column: "ScoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerTreeOrnaments");

            migrationBuilder.DropTable(
                name: "TreeMaps");

            migrationBuilder.DropTable(
                name: "TreeOrnaments");
        }
    }
}
