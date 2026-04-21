using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class FcAccContexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AccLeft",
                table: "ScoreContextExtensions",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "AccRight",
                table: "ScoreContextExtensions",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "FcAccuracy",
                table: "ScoreContextExtensions",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "FcPp",
                table: "ScoreContextExtensions",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccLeft",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "AccRight",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "FcAccuracy",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "FcPp",
                table: "ScoreContextExtensions");
        }
    }
}
