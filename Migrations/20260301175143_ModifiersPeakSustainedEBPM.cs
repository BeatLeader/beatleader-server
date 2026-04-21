using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ModifiersPeakSustainedEBPM : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "BFSPeakSustainedEBPM",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BSFPeakSustainedEBPM",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "FSPeakSustainedEBPM",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "SFPeakSustainedEBPM",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "SSPeakSustainedEBPM",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BFSPeakSustainedEBPM",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "BSFPeakSustainedEBPM",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "FSPeakSustainedEBPM",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "SFPeakSustainedEBPM",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "SSPeakSustainedEBPM",
                table: "ModifiersRating");
        }
    }
}
