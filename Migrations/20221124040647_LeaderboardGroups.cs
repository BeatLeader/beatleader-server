using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class LeaderboardGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeaderboardGroupId",
                table: "Leaderboards",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Timestamp",
                table: "Leaderboards",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "LeaderboardGroup",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardGroup", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leaderboards_LeaderboardGroupId",
                table: "Leaderboards",
                column: "LeaderboardGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Leaderboards_LeaderboardGroup_LeaderboardGroupId",
                table: "Leaderboards",
                column: "LeaderboardGroupId",
                principalTable: "LeaderboardGroup",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leaderboards_LeaderboardGroup_LeaderboardGroupId",
                table: "Leaderboards");

            migrationBuilder.DropTable(
                name: "LeaderboardGroup");

            migrationBuilder.DropIndex(
                name: "IX_Leaderboards_LeaderboardGroupId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "LeaderboardGroupId",
                table: "Leaderboards");

            migrationBuilder.DropColumn(
                name: "Timestamp",
                table: "Leaderboards");
        }
    }
}
