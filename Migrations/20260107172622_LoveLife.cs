using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class LoveLife : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdolDescriptionId",
                table: "Songs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IdolCanvases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CanvasState = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastUpdated = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdolCanvases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdolCanvases_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IdolDecorations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GloballyAvailable = table.Column<bool>(type: "bit", nullable: false),
                    SmallPictureRegular = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BigPictureRegular = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SmallPicturePro = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BigPicturePro = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SongId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdolDecorations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdolDecorations_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IdolDescriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Bonus = table.Column<bool>(type: "bit", nullable: false),
                    GloballyAvailable = table.Column<bool>(type: "bit", nullable: false),
                    Birthday = table.Column<int>(type: "int", nullable: false),
                    SmallPictureRegular = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BigPictureRegular = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SmallPicturePro = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BigPicturePro = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdolDescriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerIdolDecorations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    IdolDecorationId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerIdolDecorations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerIdolDecorations_IdolDecorations_IdolDecorationId",
                        column: x => x.IdolDecorationId,
                        principalTable: "IdolDecorations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerIdolDecorations_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PlayerBonusIdols",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    IdolDescriptionId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerBonusIdols", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerBonusIdols_IdolDescriptions_IdolDescriptionId",
                        column: x => x.IdolDescriptionId,
                        principalTable: "IdolDescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerBonusIdols_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Songs_IdolDescriptionId",
                table: "Songs",
                column: "IdolDescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_IdolCanvases_PlayerId",
                table: "IdolCanvases",
                column: "PlayerId",
                unique: true,
                filter: "[PlayerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IdolDecorations_SongId",
                table: "IdolDecorations",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBonusIdols_IdolDescriptionId",
                table: "PlayerBonusIdols",
                column: "IdolDescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerBonusIdols_PlayerId",
                table: "PlayerBonusIdols",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerIdolDecorations_IdolDecorationId",
                table: "PlayerIdolDecorations",
                column: "IdolDecorationId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerIdolDecorations_PlayerId",
                table: "PlayerIdolDecorations",
                column: "PlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Songs_IdolDescriptions_IdolDescriptionId",
                table: "Songs",
                column: "IdolDescriptionId",
                principalTable: "IdolDescriptions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Songs_IdolDescriptions_IdolDescriptionId",
                table: "Songs");

            migrationBuilder.DropTable(
                name: "IdolCanvases");

            migrationBuilder.DropTable(
                name: "PlayerBonusIdols");

            migrationBuilder.DropTable(
                name: "PlayerIdolDecorations");

            migrationBuilder.DropTable(
                name: "IdolDescriptions");

            migrationBuilder.DropTable(
                name: "IdolDecorations");

            migrationBuilder.DropIndex(
                name: "IX_Songs_IdolDescriptionId",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "IdolDescriptionId",
                table: "Songs");
        }
    }
}
