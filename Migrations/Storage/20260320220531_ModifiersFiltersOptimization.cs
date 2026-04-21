using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.Storage
{
    /// <inheritdoc />
    public partial class ModifiersFiltersOptimization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasBFS",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasBSF",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasDA",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasEZ",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasFS",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasGN",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasHD",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNA",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNB",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNF",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNO",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasOHP",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasOP",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasPM",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSA",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSC",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSF",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSMC",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSS",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasBFS",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasBSF",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasDA",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasEZ",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasFS",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasGN",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasHD",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasNA",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasNB",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasNF",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasNO",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasOHP",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasOP",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasPM",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasSA",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasSC",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasSF",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasSMC",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "HasSS",
                table: "PlayerLeaderboardStats");
        }
    }
}
