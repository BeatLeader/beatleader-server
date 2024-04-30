using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class MaxScoreGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxScoreGraphId",
                table: "DifficultyDescription",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MaxScoreGraph",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Graph = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxScoreGraph", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DifficultyDescription_MaxScoreGraphId",
                table: "DifficultyDescription",
                column: "MaxScoreGraphId");

            migrationBuilder.AddForeignKey(
                name: "FK_DifficultyDescription_MaxScoreGraph_MaxScoreGraphId",
                table: "DifficultyDescription",
                column: "MaxScoreGraphId",
                principalTable: "MaxScoreGraph",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DifficultyDescription_MaxScoreGraph_MaxScoreGraphId",
                table: "DifficultyDescription");

            migrationBuilder.DropTable(
                name: "MaxScoreGraph");

            migrationBuilder.DropIndex(
                name: "IX_DifficultyDescription_MaxScoreGraphId",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "MaxScoreGraphId",
                table: "DifficultyDescription");
        }
    }
}
