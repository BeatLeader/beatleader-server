using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class RichPresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RichPresenceEnabled",
                table: "ProfileSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StreamingCommentPermissions",
                table: "ProfileSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StreamingViewPermissions",
                table: "ProfileSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RichPresenceId",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RichPresence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityStatus = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RichPresence", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Players_RichPresenceId",
                table: "Players",
                column: "RichPresenceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_RichPresence_RichPresenceId",
                table: "Players",
                column: "RichPresenceId",
                principalTable: "RichPresence",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_RichPresence_RichPresenceId",
                table: "Players");

            migrationBuilder.DropTable(
                name: "RichPresence");

            migrationBuilder.DropIndex(
                name: "IX_Players_RichPresenceId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "RichPresenceEnabled",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "StreamingCommentPermissions",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "StreamingViewPermissions",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "RichPresenceId",
                table: "Players");
        }
    }
}
