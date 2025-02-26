using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.Storage
{
    /// <inheritdoc />
    public partial class PinnedAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MetadataId",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScoreMetadata",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PinnedContexts = table.Column<int>(type: "int", nullable: false),
                    HighlightedInfo = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkService = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkServiceIcon = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Link = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreMetadata", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_MetadataId",
                table: "PlayerLeaderboardStats",
                column: "MetadataId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerLeaderboardStats_ScoreMetadata_MetadataId",
                table: "PlayerLeaderboardStats",
                column: "MetadataId",
                principalTable: "ScoreMetadata",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerLeaderboardStats_ScoreMetadata_MetadataId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropTable(
                name: "ScoreMetadata");

            migrationBuilder.DropIndex(
                name: "IX_PlayerLeaderboardStats_MetadataId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "MetadataId",
                table: "PlayerLeaderboardStats");
        }
    }
}
