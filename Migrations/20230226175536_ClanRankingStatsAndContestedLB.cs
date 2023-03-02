using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ClanRankingStatsAndContestedLB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClanRanking_Clans_ClanId",
                table: "ClanRanking");

            migrationBuilder.DropForeignKey(
                name: "FK_ClanRanking_Leaderboards_LeaderboardId",
                table: "ClanRanking");

            migrationBuilder.AddColumn<bool>(
                name: "ClanRankingContested",
                table: "Leaderboards",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "LeaderboardId",
                table: "ClanRanking",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClanId",
                table: "ClanRanking",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ClanAverageAccuracy",
                table: "ClanRanking",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "ClanAverageRank",
                table: "ClanRanking",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "ClanTotalScore",
                table: "ClanRanking",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddForeignKey(
                name: "FK_ClanRanking_Clans_ClanId",
                table: "ClanRanking",
                column: "ClanId",
                principalTable: "Clans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ClanRanking_Leaderboards_LeaderboardId",
                table: "ClanRanking",
                column: "LeaderboardId",
                principalTable: "Leaderboards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClanRanking_Clans_ClanId",
                table: "ClanRanking");

            migrationBuilder.DropForeignKey(
                name: "FK_ClanRanking_Leaderboards_LeaderboardId",
                table: "ClanRanking");

            migrationBuilder.DropColumn(
                name: "ClanRankingContested",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "ClanAverageAccuracy",
                table: "ClanRanking");

            migrationBuilder.DropColumn(
                name: "ClanAverageRank",
                table: "ClanRanking");

            migrationBuilder.DropColumn(
                name: "ClanTotalScore",
                table: "ClanRanking");

            migrationBuilder.AlterColumn<string>(
                name: "LeaderboardId",
                table: "ClanRanking",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<int>(
                name: "ClanId",
                table: "ClanRanking",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_ClanRanking_Clans_ClanId",
                table: "ClanRanking",
                column: "ClanId",
                principalTable: "Clans",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ClanRanking_Leaderboards_LeaderboardId",
                table: "ClanRanking",
                column: "LeaderboardId",
                principalTable: "Leaderboards",
                principalColumn: "Id");
        }
    }
}
