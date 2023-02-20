using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class QualityVoting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ForumChannelId",
                table: "RankQualification");

            migrationBuilder.DropColumn(
                name: "ForumMessage",
                table: "QualificationCommentary");

            migrationBuilder.CreateTable(
                name: "QualificationVote",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false),
                    EditTimeset = table.Column<int>(type: "int", nullable: true),
                    Edited = table.Column<bool>(type: "bit", nullable: false),
                    RankQualificationId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualificationVote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualificationVote_RankQualification_RankQualificationId",
                        column: x => x.RankQualificationId,
                        principalTable: "RankQualification",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QualificationVote_RankQualificationId",
                table: "QualificationVote",
                column: "RankQualificationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QualificationVote");

            migrationBuilder.AddColumn<decimal>(
                name: "ForumChannelId",
                table: "RankQualification",
                type: "decimal(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ForumMessage",
                table: "QualificationCommentary",
                type: "decimal(20,0)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
