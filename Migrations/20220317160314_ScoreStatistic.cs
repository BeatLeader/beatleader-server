using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    public partial class ScoreStatistic : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedTime",
                table: "Songs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Qualified",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "QualifiedTime",
                table: "DifficultyDescription",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RankedTime",
                table: "DifficultyDescription",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AccuracyTracker",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccRight = table.Column<float>(type: "real", nullable: false),
                    AccLeft = table.Column<float>(type: "real", nullable: false),
                    LeftPreswing = table.Column<float>(type: "real", nullable: false),
                    RightPreswing = table.Column<float>(type: "real", nullable: false),
                    AveragePreswing = table.Column<float>(type: "real", nullable: false),
                    LeftPostswing = table.Column<float>(type: "real", nullable: false),
                    RightPostswing = table.Column<float>(type: "real", nullable: false),
                    LeftTimeDependence = table.Column<float>(type: "real", nullable: false),
                    RightTimeDependence = table.Column<float>(type: "real", nullable: false),
                    LeftAverageCutS = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RightAverageCutS = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GridAccS = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccuracyTracker", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HitTracker",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaxCombo = table.Column<int>(type: "int", nullable: false),
                    LeftMiss = table.Column<int>(type: "int", nullable: false),
                    RightMiss = table.Column<int>(type: "int", nullable: false),
                    LeftBadCuts = table.Column<int>(type: "int", nullable: false),
                    RightBadCuts = table.Column<int>(type: "int", nullable: false),
                    LeftBombs = table.Column<int>(type: "int", nullable: false),
                    RightBombs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HitTracker", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScoreGraphTracker",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GraphS = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreGraphTracker", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WinTracker",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Won = table.Column<bool>(type: "bit", nullable: false),
                    EndTime = table.Column<float>(type: "real", nullable: false),
                    NbOfPause = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WinTracker", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScoreStatistics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScoreId = table.Column<int>(type: "int", nullable: false),
                    HitTrackerId = table.Column<int>(type: "int", nullable: false),
                    AccuracyTrackerId = table.Column<int>(type: "int", nullable: false),
                    WinTrackerId = table.Column<int>(type: "int", nullable: false),
                    ScoreGraphTrackerId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreStatistics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoreStatistics_AccuracyTracker_AccuracyTrackerId",
                        column: x => x.AccuracyTrackerId,
                        principalTable: "AccuracyTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScoreStatistics_HitTracker_HitTrackerId",
                        column: x => x.HitTrackerId,
                        principalTable: "HitTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScoreStatistics_ScoreGraphTracker_ScoreGraphTrackerId",
                        column: x => x.ScoreGraphTrackerId,
                        principalTable: "ScoreGraphTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScoreStatistics_WinTracker_WinTrackerId",
                        column: x => x.WinTrackerId,
                        principalTable: "WinTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScoreStatistics_AccuracyTrackerId",
                table: "ScoreStatistics",
                column: "AccuracyTrackerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreStatistics_HitTrackerId",
                table: "ScoreStatistics",
                column: "HitTrackerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreStatistics_ScoreGraphTrackerId",
                table: "ScoreStatistics",
                column: "ScoreGraphTrackerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreStatistics_WinTrackerId",
                table: "ScoreStatistics",
                column: "WinTrackerId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScoreStatistics");

            migrationBuilder.DropTable(
                name: "AccuracyTracker");

            migrationBuilder.DropTable(
                name: "HitTracker");

            migrationBuilder.DropTable(
                name: "ScoreGraphTracker");

            migrationBuilder.DropTable(
                name: "WinTracker");

            migrationBuilder.DropColumn(
                name: "CreatedTime",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "Qualified",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "QualifiedTime",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "RankedTime",
                table: "DifficultyDescription");
        }
    }
}
