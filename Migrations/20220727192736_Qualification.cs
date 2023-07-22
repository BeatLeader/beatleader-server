using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class Qualification : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Qualification",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "QualificationId",
                table: "Leaderboards",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "RankedTime",
                table: "DifficultyDescription",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<int>(
                name: "QualifiedTime",
                table: "DifficultyDescription",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateTable(
                name: "RankQualification",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    MapperAllowed = table.Column<bool>(type: "bit", nullable: false),
                    RTMember = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankQualification", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VoterFeedback",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RTMember = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Value = table.Column<float>(type: "real", nullable: false),
                    RankVotingScoreId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoterFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoterFeedback_RankVotings_RankVotingScoreId",
                        column: x => x.RankVotingScoreId,
                        principalTable: "RankVotings",
                        principalColumn: "ScoreId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leaderboards_QualificationId",
                table: "Leaderboards",
                column: "QualificationId");

            migrationBuilder.CreateIndex(
                name: "IX_VoterFeedback_RankVotingScoreId",
                table: "VoterFeedback",
                column: "RankVotingScoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Leaderboards_RankQualification_QualificationId",
                table: "Leaderboards",
                column: "QualificationId",
                principalTable: "RankQualification",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leaderboards_RankQualification_QualificationId",
                table: "Leaderboards");

            migrationBuilder.DropTable(
                name: "RankQualification");

            migrationBuilder.DropTable(
                name: "VoterFeedback");

            migrationBuilder.DropIndex(
                name: "IX_Leaderboards_QualificationId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "Qualification",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "QualificationId",
                table: "Leaderboards");

            migrationBuilder.AlterColumn<string>(
                name: "RankedTime",
                table: "DifficultyDescription",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "QualifiedTime",
                table: "DifficultyDescription",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
