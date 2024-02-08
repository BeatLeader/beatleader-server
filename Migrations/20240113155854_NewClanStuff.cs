using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class NewClanStuff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClanRankingDiscordHook",
                table: "Clans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerChangesCallback",
                table: "Clans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rank",
                table: "Clans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "TotalScore",
                table: "ClanRanking",
                type: "int",
                nullable: false,
                oldClrType: typeof(float),
                oldType: "real");

            migrationBuilder.AddColumn<int>(
                name: "Rank",
                table: "ClanRanking",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClanRankingDiscordHook",
                table: "Clans");

            migrationBuilder.DropColumn(
                name: "PlayerChangesCallback",
                table: "Clans");

            migrationBuilder.DropColumn(
                name: "Rank",
                table: "Clans");

            migrationBuilder.DropColumn(
                name: "Rank",
                table: "ClanRanking");

            migrationBuilder.AlterColumn<float>(
                name: "TotalScore",
                table: "ClanRanking",
                type: "real",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
