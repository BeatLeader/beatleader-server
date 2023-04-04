using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ModifiersStars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "FSStars",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "SFStars",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "SSStars",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FSStars",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "SFStars",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "SSStars",
                table: "ModifiersRating");
        }
    }
}
