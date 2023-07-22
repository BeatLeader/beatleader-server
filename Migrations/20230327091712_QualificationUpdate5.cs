using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class QualificationUpdate5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "NewAccRating",
                table: "QualificationChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "NewPassRating",
                table: "QualificationChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "NewTechRating",
                table: "QualificationChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "OldAccRating",
                table: "QualificationChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "OldPassRating",
                table: "QualificationChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "OldTechRating",
                table: "QualificationChange",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewAccRating",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "NewPassRating",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "NewTechRating",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "OldAccRating",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "OldPassRating",
                table: "QualificationChange");

            migrationBuilder.DropColumn(
                name: "OldTechRating",
                table: "QualificationChange");
        }
    }
}
