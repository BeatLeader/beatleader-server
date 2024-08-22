using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class BetterSpeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "BFSAccRating",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BFSPassRating",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BFSPredictedAcc",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BFSStars",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BFSTechRating",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BSFAccRating",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BSFPassRating",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BSFPredictedAcc",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BSFStars",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BSFTechRating",
                table: "ModifiersRating",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AlterColumn<string>(
                name: "Tag",
                table: "Clans",
                type: "nvarchar(6)",
                maxLength: 6,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "LeaderID",
                table: "Clans",
                type: "nvarchar(25)",
                maxLength: 25,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "Clans",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Clans_Tag",
                table: "Clans",
                column: "Tag",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clans_Tag",
                table: "Clans");

            migrationBuilder.DropColumn(
                name: "BFSAccRating",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "BFSPassRating",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "BFSPredictedAcc",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "BFSStars",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "BFSTechRating",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "BSFAccRating",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "BSFPassRating",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "BSFPredictedAcc",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "BSFStars",
                table: "ModifiersRating");

            migrationBuilder.DropColumn(
                name: "BSFTechRating",
                table: "ModifiersRating");

            migrationBuilder.AlterColumn<string>(
                name: "Tag",
                table: "Clans",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(6)",
                oldMaxLength: 6);

            migrationBuilder.AlterColumn<string>(
                name: "LeaderID",
                table: "Clans",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(25)",
                oldMaxLength: 25);

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "Clans",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);
        }
    }
}
