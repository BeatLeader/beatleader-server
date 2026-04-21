using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class LeftHandedInScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LeftHanded",
                table: "Scores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DifficultyDescriptionExtension",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Context = table.Column<int>(type: "int", nullable: false),
                    MaxScoreRight = table.Column<int>(type: "int", nullable: false),
                    MaxScoreLeft = table.Column<int>(type: "int", nullable: false),
                    DifficultyDescriptionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DifficultyDescriptionExtension", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DifficultyDescriptionExtension_DifficultyDescription_DifficultyDescriptionId",
                        column: x => x.DifficultyDescriptionId,
                        principalTable: "DifficultyDescription",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DifficultyDescriptionExtension_DifficultyDescriptionId",
                table: "DifficultyDescriptionExtension",
                column: "DifficultyDescriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DifficultyDescriptionExtension");

            migrationBuilder.DropColumn(
                name: "LeftHanded",
                table: "Scores");
        }
    }
}
