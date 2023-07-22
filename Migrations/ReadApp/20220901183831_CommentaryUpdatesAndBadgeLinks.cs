using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class CommentaryUpdatesAndBadgeLinks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NewCriteriaCommentary",
                table: "QualificationChange",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldCriteriaCommentary",
                table: "QualificationChange",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Link",
                table: "Badges",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewCriteriaCommentary",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "OldCriteriaCommentary",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "Link",
                table: "Badges");
        }
    }
}
