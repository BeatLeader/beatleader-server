using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class QualificationUpdate1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RTVotes",
                table: "RankQualification",
                newName: "ApprovalTimeset");

            migrationBuilder.AddColumn<bool>(
                name: "Approved",
                table: "RankQualification",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Approvers",
                table: "RankQualification",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Approved",
                table: "RankQualification");

            migrationBuilder.DropColumn(
                name: "Approvers",
                table: "RankQualification");

            migrationBuilder.RenameColumn(
                name: "ApprovalTimeset",
                table: "RankQualification",
                newName: "RTVotes");
        }
    }
}
