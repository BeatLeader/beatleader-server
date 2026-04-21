using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ModifiersFiltersOptimization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasBFS",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasBSF",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasDA",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasEZ",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasFS",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasGN",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasHD",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNA",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNB",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNF",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNO",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasOHP",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasOP",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasPM",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSA",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSC",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSF",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSMC",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSS",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasBFS",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasBSF",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasDA",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasEZ",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasFS",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasGN",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasHD",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNA",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNB",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNF",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNO",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasOHP",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasOP",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasPM",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSA",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSC",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSF",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSMC",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSS",
                table: "ScoreContextExtensions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasBFS",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasBSF",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasDA",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasEZ",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasFS",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasGN",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasHD",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasNA",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasNB",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasNF",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasNO",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasOHP",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasOP",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasPM",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasSA",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasSC",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasSF",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasSMC",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasSS",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "HasBFS",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasBSF",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasDA",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasEZ",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasFS",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasGN",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasHD",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasNA",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasNB",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasNF",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasNO",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasOHP",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasOP",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasPM",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasSA",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasSC",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasSF",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasSMC",
                table: "ScoreContextExtensions");

            migrationBuilder.DropColumn(
                name: "HasSS",
                table: "ScoreContextExtensions");
        }
    }
}
