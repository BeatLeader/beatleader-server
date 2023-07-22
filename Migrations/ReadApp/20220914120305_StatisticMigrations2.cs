using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class StatisticMigrations2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
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
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccuracyTracker",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccLeft = table.Column<float>(type: "real", nullable: false),
                    AccRight = table.Column<float>(type: "real", nullable: false),
                    AveragePreswing = table.Column<float>(type: "real", nullable: false),
                    GridAccS = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeftAverageCutS = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeftPostswing = table.Column<float>(type: "real", nullable: false),
                    LeftPreswing = table.Column<float>(type: "real", nullable: false),
                    LeftTimeDependence = table.Column<float>(type: "real", nullable: false),
                    RightAverageCutS = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RightPostswing = table.Column<float>(type: "real", nullable: false),
                    RightPreswing = table.Column<float>(type: "real", nullable: false),
                    RightTimeDependence = table.Column<float>(type: "real", nullable: false)
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
                    LeftBadCuts = table.Column<int>(type: "int", nullable: false),
                    LeftBombs = table.Column<int>(type: "int", nullable: false),
                    LeftMiss = table.Column<int>(type: "int", nullable: false),
                    MaxCombo = table.Column<int>(type: "int", nullable: false),
                    RightBadCuts = table.Column<int>(type: "int", nullable: false),
                    RightBombs = table.Column<int>(type: "int", nullable: false),
                    RightMiss = table.Column<int>(type: "int", nullable: false)
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
                    AverageHeight = table.Column<float>(type: "real", nullable: false),
                    EndTime = table.Column<float>(type: "real", nullable: false),
                    JumpDistance = table.Column<float>(type: "real", nullable: false),
                    NbOfPause = table.Column<int>(type: "int", nullable: false),
                    TotalScore = table.Column<int>(type: "int", nullable: false),
                    Won = table.Column<bool>(type: "bit", nullable: false)
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
                    AccuracyTrackerId = table.Column<int>(type: "int", nullable: false),
                    HitTrackerId = table.Column<int>(type: "int", nullable: false),
                    ScoreGraphTrackerId = table.Column<int>(type: "int", nullable: false),
                    WinTrackerId = table.Column<int>(type: "int", nullable: false),
                    ScoreId = table.Column<int>(type: "int", nullable: false)
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
    }
}
