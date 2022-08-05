using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class QualificationUpdate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CriteriaChecker",
                table: "RankQualification",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CriteriaCommentary",
                table: "RankQualification",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CriteriaMet",
                table: "RankQualification",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CriteriaTimeset",
                table: "RankQualification",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MapperId",
                table: "RankQualification",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MapperQualification",
                table: "RankQualification",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RTVotes",
                table: "RankQualification",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CriteriaChecker",
                table: "RankQualification");

            migrationBuilder.DropColumn(
                name: "CriteriaCommentary",
                table: "RankQualification");

            migrationBuilder.DropColumn(
                name: "CriteriaMet",
                table: "RankQualification");

            migrationBuilder.DropColumn(
                name: "CriteriaTimeset",
                table: "RankQualification");

            migrationBuilder.DropColumn(
                name: "MapperId",
                table: "RankQualification");

            migrationBuilder.DropColumn(
                name: "MapperQualification",
                table: "RankQualification");

            migrationBuilder.DropColumn(
                name: "RTVotes",
                table: "RankQualification");
        }
    }
}
