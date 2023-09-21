using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ContextsBannedBool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Banned",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Qualification",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Banned",
                table: "PlayerContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Banned",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "Qualification",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "Banned",
                table: "PlayerContextExtensions");
        }
    }
}
