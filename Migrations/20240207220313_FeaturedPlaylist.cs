using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class FeaturedPlaylist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeaturedPlaylist",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlaylistLink = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Cover = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OwnerCover = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OwnerLink = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeaturedPlaylist", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalMapChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeaderboardId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    OldX = table.Column<float>(type: "real", nullable: false),
                    OldY = table.Column<float>(type: "real", nullable: false),
                    OldClan1Id = table.Column<int>(type: "int", nullable: true),
                    OldClan1Capture = table.Column<float>(type: "real", nullable: true),
                    OldClan1Pp = table.Column<float>(type: "real", nullable: true),
                    OldClan2Id = table.Column<int>(type: "int", nullable: true),
                    OldClan2Capture = table.Column<float>(type: "real", nullable: true),
                    OldClan2Pp = table.Column<float>(type: "real", nullable: true),
                    OldClan3Id = table.Column<int>(type: "int", nullable: true),
                    OldClan3Capture = table.Column<float>(type: "real", nullable: true),
                    OldClan3Pp = table.Column<float>(type: "real", nullable: true),
                    NewX = table.Column<float>(type: "real", nullable: false),
                    NewY = table.Column<float>(type: "real", nullable: false),
                    NewClan1Id = table.Column<int>(type: "int", nullable: true),
                    NewClan1Capture = table.Column<float>(type: "real", nullable: true),
                    NewClan1Pp = table.Column<float>(type: "real", nullable: true),
                    NewClan2Id = table.Column<int>(type: "int", nullable: true),
                    NewClan2Capture = table.Column<float>(type: "real", nullable: true),
                    NewClan2Pp = table.Column<float>(type: "real", nullable: true),
                    NewClan3Id = table.Column<int>(type: "int", nullable: true),
                    NewClan3Capture = table.Column<float>(type: "real", nullable: true),
                    NewClan3Pp = table.Column<float>(type: "real", nullable: true),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlayerAction = table.Column<int>(type: "int", nullable: true),
                    ScoreId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalMapChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClanFeaturedPlaylist",
                columns: table => new
                {
                    ClansId = table.Column<int>(type: "int", nullable: false),
                    FeaturedPlaylistsId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanFeaturedPlaylist", x => new { x.ClansId, x.FeaturedPlaylistsId });
                    table.ForeignKey(
                        name: "FK_ClanFeaturedPlaylist_Clans_ClansId",
                        column: x => x.ClansId,
                        principalTable: "Clans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClanFeaturedPlaylist_FeaturedPlaylist_FeaturedPlaylistsId",
                        column: x => x.FeaturedPlaylistsId,
                        principalTable: "FeaturedPlaylist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeaturedPlaylistLeaderboard",
                columns: table => new
                {
                    FeaturedPlaylistsId = table.Column<int>(type: "int", nullable: false),
                    LeaderboardsId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeaturedPlaylistLeaderboard", x => new { x.FeaturedPlaylistsId, x.LeaderboardsId });
                    table.ForeignKey(
                        name: "FK_FeaturedPlaylistLeaderboard_FeaturedPlaylist_FeaturedPlaylistsId",
                        column: x => x.FeaturedPlaylistsId,
                        principalTable: "FeaturedPlaylist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FeaturedPlaylistLeaderboard_Leaderboards_LeaderboardsId",
                        column: x => x.LeaderboardsId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClanFeaturedPlaylist_FeaturedPlaylistsId",
                table: "ClanFeaturedPlaylist",
                column: "FeaturedPlaylistsId");

            migrationBuilder.CreateIndex(
                name: "IX_FeaturedPlaylistLeaderboard_LeaderboardsId",
                table: "FeaturedPlaylistLeaderboard",
                column: "LeaderboardsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClanFeaturedPlaylist");

            migrationBuilder.DropTable(
                name: "FeaturedPlaylistLeaderboard");

            migrationBuilder.DropTable(
                name: "GlobalMapChanges");

            migrationBuilder.DropTable(
                name: "FeaturedPlaylist");
        }
    }
}
