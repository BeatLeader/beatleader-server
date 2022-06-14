using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class RankChanges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Timeset",
                table: "RankVotings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "DifficultyDescription",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RankChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Diff = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldRankability = table.Column<float>(type: "real", nullable: false),
                    OldStars = table.Column<float>(type: "real", nullable: false),
                    OldType = table.Column<int>(type: "int", nullable: false),
                    NewRankability = table.Column<float>(type: "real", nullable: false),
                    NewStars = table.Column<float>(type: "real", nullable: false),
                    NewType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankChanges", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankChanges");

            migrationBuilder.DropColumn(
                name: "Timeset",
                table: "RankVotings");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "DifficultyDescription");
        }
    }
}
