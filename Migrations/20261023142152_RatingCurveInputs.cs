using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class RatingCurveInputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "FSSliderPercentage",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "SFSliderPercentage",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "SSSliderPercentage",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "SliderPercentage",
                table: "DifficultyDescription",
                type: "real",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FSSliderPercentage",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "SFSliderPercentage",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "SSSliderPercentage",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "SliderPercentage",
                table: "DifficultyDescription");
        }
    }
}
