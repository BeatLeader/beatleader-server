using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class CriteriaComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscordRTChannelId",
                table: "RankQualification",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CriteriaCommentary",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EditTimeset = table.Column<int>(type: "int", nullable: true),
                    Edited = table.Column<bool>(type: "bit", nullable: false),
                    RankQualificationId = table.Column<int>(type: "int", nullable: true),
                    DiscordMessageId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriteriaCommentary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CriteriaCommentary_RankQualification_RankQualificationId",
                        column: x => x.RankQualificationId,
                        principalTable: "RankQualification",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CriteriaCommentary_RankQualificationId",
                table: "CriteriaCommentary",
                column: "RankQualificationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CriteriaCommentary");

            migrationBuilder.DropColumn(
                name: "DiscordRTChannelId",
                table: "RankQualification");
        }
    }
}
