using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class Mappers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Mappers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Avatar = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Curator = table.Column<bool>(type: "bit", nullable: true),
                    VerifiedMapper = table.Column<bool>(type: "bit", nullable: false),
                    PlaylistUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mappers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MapperSong",
                columns: table => new
                {
                    MappersId = table.Column<int>(type: "int", nullable: false),
                    SongsId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapperSong", x => new { x.MappersId, x.SongsId });
                    table.ForeignKey(
                        name: "FK_MapperSong_Mappers_MappersId",
                        column: x => x.MappersId,
                        principalTable: "Mappers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MapperSong_Songs_SongsId",
                        column: x => x.SongsId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MapperSong_SongsId",
                table: "MapperSong",
                column: "SongsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapperSong");

            migrationBuilder.DropTable(
                name: "Mappers");
        }
    }
}
