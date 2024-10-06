using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.Storage
{
    /// <inheritdoc />
    public partial class StartTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "StartTime",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "PlayerLeaderboardStats");
        }
    }
}
