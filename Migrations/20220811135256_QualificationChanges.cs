using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class QualificationChanges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QualificationChange",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldRankability = table.Column<float>(type: "real", nullable: false),
                    OldStars = table.Column<float>(type: "real", nullable: false),
                    OldType = table.Column<int>(type: "int", nullable: false),
                    OldCriteriaMet = table.Column<int>(type: "int", nullable: false),
                    NewRankability = table.Column<float>(type: "real", nullable: false),
                    NewStars = table.Column<float>(type: "real", nullable: false),
                    NewType = table.Column<int>(type: "int", nullable: false),
                    NewCriteriaMet = table.Column<int>(type: "int", nullable: false),
                    RankQualificationId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualificationChange", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualificationChange_RankQualification_RankQualificationId",
                        column: x => x.RankQualificationId,
                        principalTable: "RankQualification",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QualificationChange_RankQualificationId",
                table: "QualificationChange",
                column: "RankQualificationId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QualificationChange");
        }
    }
}
