using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class QualificationCommentary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CriteriaCheck",
                table: "RankQualification",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QualificationCommentary",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EditTimeset = table.Column<int>(type: "int", nullable: true),
                    Edited = table.Column<bool>(type: "bit", nullable: false),
                    RankQualificationId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualificationCommentary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualificationCommentary_RankQualification_RankQualificationId",
                        column: x => x.RankQualificationId,
                        principalTable: "RankQualification",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QualificationCommentary_RankQualificationId",
                table: "QualificationCommentary",
                column: "RankQualificationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QualificationCommentary");

            migrationBuilder.DropColumn(
                name: "CriteriaCheck",
                table: "RankQualification");
        }
    }
}
