using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ClanRankingExtraStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClanRankingId",
                table: "Scores",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClanRank",
                table: "ClanRanking",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastUpdateTime",
                table: "ClanRanking",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_ClanRankingId",
                table: "Scores",
                column: "ClanRankingId");

            migrationBuilder.AddForeignKey(
                name: "FK_Scores_ClanRanking_ClanRankingId",
                table: "Scores",
                column: "ClanRankingId",
                principalTable: "ClanRanking",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Scores_ClanRanking_ClanRankingId",
                table: "Scores");

            migrationBuilder.DropIndex(
                name: "IX_Scores_ClanRankingId",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "ClanRankingId",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "ClanRank",
                table: "ClanRanking");

            migrationBuilder.DropColumn(
                name: "LastUpdateTime",
                table: "ClanRanking");
        }
    }
}
