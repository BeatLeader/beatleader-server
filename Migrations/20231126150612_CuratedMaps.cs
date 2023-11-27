using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class CuratedMaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CollaboratorIds",
                table: "Songs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalStatus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Link = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Responsible = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SongId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalStatus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalStatus_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SongsLastUpdateTimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongsLastUpdateTimes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_Timepost",
                table: "Scores",
                column: "Timepost");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalStatus_SongId",
                table: "ExternalStatus",
                column: "SongId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalStatus");

            migrationBuilder.DropTable(
                name: "SongsLastUpdateTimes");

            migrationBuilder.DropIndex(
                name: "IX_Scores_Timepost",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "CollaboratorIds",
                table: "Songs");
        }
    }
}
