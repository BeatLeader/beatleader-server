using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class AnalyzerDiffStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DifficultyStatisticsId",
                table: "DifficultyDescription",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "LinearPercentage",
                table: "DifficultyDescription",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "MultiRating",
                table: "DifficultyDescription",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "PeakSustainedEBPM",
                table: "DifficultyDescription",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TypeBombReset",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TypeFitbeat",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TypeLinear",
                table: "DifficultyDescription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DifficultyStatistics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Stacks = table.Column<int>(type: "int", nullable: false),
                    Towers = table.Column<int>(type: "int", nullable: false),
                    Sliders = table.Column<int>(type: "int", nullable: false),
                    CurvedSliders = table.Column<int>(type: "int", nullable: false),
                    Windows = table.Column<int>(type: "int", nullable: false),
                    SlantedWindows = table.Column<int>(type: "int", nullable: false),
                    DodgeWalls = table.Column<int>(type: "int", nullable: false),
                    CrouchWalls = table.Column<int>(type: "int", nullable: false),
                    ParityErrors = table.Column<int>(type: "int", nullable: false),
                    BombAvoidances = table.Column<int>(type: "int", nullable: false),
                    LinearSwings = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DifficultyStatistics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MapSwingData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BpmTime = table.Column<double>(type: "float", nullable: false),
                    Direction = table.Column<double>(type: "float", nullable: false),
                    Forehand = table.Column<bool>(type: "bit", nullable: false),
                    ParityErrors = table.Column<bool>(type: "bit", nullable: false),
                    BombAvoidance = table.Column<bool>(type: "bit", nullable: false),
                    IsLinear = table.Column<bool>(type: "bit", nullable: false),
                    AngleStrain = table.Column<double>(type: "float", nullable: false),
                    RepositioningDistance = table.Column<double>(type: "float", nullable: false),
                    RotationAmount = table.Column<double>(type: "float", nullable: false),
                    SwingFrequency = table.Column<double>(type: "float", nullable: false),
                    DistanceDiff = table.Column<double>(type: "float", nullable: false),
                    SwingSpeed = table.Column<double>(type: "float", nullable: false),
                    HitDistance = table.Column<double>(type: "float", nullable: false),
                    Stress = table.Column<double>(type: "float", nullable: false),
                    LowSpeedFalloff = table.Column<double>(type: "float", nullable: false),
                    StressMultiplier = table.Column<double>(type: "float", nullable: false),
                    NjsBuff = table.Column<double>(type: "float", nullable: false),
                    WallBuff = table.Column<double>(type: "float", nullable: false),
                    IsStream = table.Column<bool>(type: "bit", nullable: false),
                    SwingDiff = table.Column<double>(type: "float", nullable: false),
                    SwingTech = table.Column<double>(type: "float", nullable: false),
                    DifficultyStatisticsId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapSwingData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MapSwingData_DifficultyStatistics_DifficultyStatisticsId",
                        column: x => x.DifficultyStatisticsId,
                        principalTable: "DifficultyStatistics",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DifficultyDescription_DifficultyStatisticsId",
                table: "DifficultyDescription",
                column: "DifficultyStatisticsId");

            migrationBuilder.CreateIndex(
                name: "IX_MapSwingData_DifficultyStatisticsId",
                table: "MapSwingData",
                column: "DifficultyStatisticsId");

            migrationBuilder.AddForeignKey(
                name: "FK_DifficultyDescription_DifficultyStatistics_DifficultyStatisticsId",
                table: "DifficultyDescription",
                column: "DifficultyStatisticsId",
                principalTable: "DifficultyStatistics",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DifficultyDescription_DifficultyStatistics_DifficultyStatisticsId",
                table: "DifficultyDescription");

            migrationBuilder.DropTable(
                name: "MapSwingData");

            migrationBuilder.DropTable(
                name: "DifficultyStatistics");

            migrationBuilder.DropIndex(
                name: "IX_DifficultyDescription_DifficultyStatisticsId",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "DifficultyStatisticsId",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "LinearPercentage",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "MultiRating",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "PeakSustainedEBPM",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "TypeBombReset",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "TypeFitbeat",
                table: "DifficultyDescription");

            migrationBuilder.DropColumn(
                name: "TypeLinear",
                table: "DifficultyDescription");
        }
    }
}
