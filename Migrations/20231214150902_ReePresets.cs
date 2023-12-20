using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ReePresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReePresetDownloads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PresetId = table.Column<int>(type: "int", nullable: false),
                    Player = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReePresetDownloads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReeSabersPresets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CoverLink = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JsonLinks = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextureLinks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tags = table.Column<int>(type: "int", nullable: false),
                    Timeposted = table.Column<int>(type: "int", nullable: false),
                    Timeupdated = table.Column<int>(type: "int", nullable: false),
                    DownloadsCount = table.Column<int>(type: "int", nullable: false),
                    QuestDownloadsCount = table.Column<int>(type: "int", nullable: false),
                    PCDownloadsCount = table.Column<int>(type: "int", nullable: false),
                    ReactionsCount = table.Column<int>(type: "int", nullable: false),
                    CommentsCount = table.Column<int>(type: "int", nullable: false),
                    RemixId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReeSabersPresets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReeSabersPresets_Players_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReeSabersPresets_ReeSabersPresets_RemixId",
                        column: x => x.RemixId,
                        principalTable: "ReeSabersPresets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReeSabersComment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReeSabersPresetId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReeSabersComment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReeSabersComment_Players_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReeSabersComment_ReeSabersPresets_ReeSabersPresetId",
                        column: x => x.ReeSabersPresetId,
                        principalTable: "ReeSabersPresets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReeSabersReaction",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Reaction = table.Column<int>(type: "int", nullable: false),
                    ReeSabersCommentId = table.Column<int>(type: "int", nullable: true),
                    ReeSabersPresetId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReeSabersReaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReeSabersReaction_Players_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReeSabersReaction_ReeSabersComment_ReeSabersCommentId",
                        column: x => x.ReeSabersCommentId,
                        principalTable: "ReeSabersComment",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReeSabersReaction_ReeSabersPresets_ReeSabersPresetId",
                        column: x => x.ReeSabersPresetId,
                        principalTable: "ReeSabersPresets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReeSabersComment_AuthorId",
                table: "ReeSabersComment",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_ReeSabersComment_ReeSabersPresetId",
                table: "ReeSabersComment",
                column: "ReeSabersPresetId");

            migrationBuilder.CreateIndex(
                name: "IX_ReeSabersPresets_OwnerId",
                table: "ReeSabersPresets",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReeSabersPresets_RemixId",
                table: "ReeSabersPresets",
                column: "RemixId");

            migrationBuilder.CreateIndex(
                name: "IX_ReeSabersReaction_AuthorId",
                table: "ReeSabersReaction",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_ReeSabersReaction_ReeSabersCommentId",
                table: "ReeSabersReaction",
                column: "ReeSabersCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReeSabersReaction_ReeSabersPresetId",
                table: "ReeSabersReaction",
                column: "ReeSabersPresetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReePresetDownloads");

            migrationBuilder.DropTable(
                name: "ReeSabersReaction");

            migrationBuilder.DropTable(
                name: "ReeSabersComment");

            migrationBuilder.DropTable(
                name: "ReeSabersPresets");
        }
    }
}
