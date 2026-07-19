using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class ReplayStorageTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.DropTable(
            //    name: "MapSwingData");

            migrationBuilder.AddColumn<int>(
                name: "StorageTier",
                table: "Scores",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageTier",
                table: "Scores");

            migrationBuilder.CreateTable(
                name: "MapSwingData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AngleStrain = table.Column<double>(type: "float", nullable: false),
                    BombAvoidance = table.Column<bool>(type: "bit", nullable: false),
                    BpmTime = table.Column<double>(type: "float", nullable: false),
                    DifficultyStatisticsId = table.Column<int>(type: "int", nullable: true),
                    Direction = table.Column<double>(type: "float", nullable: false),
                    DistanceDiff = table.Column<double>(type: "float", nullable: false),
                    Forehand = table.Column<bool>(type: "bit", nullable: false),
                    HitDistance = table.Column<double>(type: "float", nullable: false),
                    IsLinear = table.Column<bool>(type: "bit", nullable: false),
                    IsStream = table.Column<bool>(type: "bit", nullable: false),
                    LowSpeedFalloff = table.Column<double>(type: "float", nullable: false),
                    NjsBuff = table.Column<double>(type: "float", nullable: false),
                    ParityErrors = table.Column<bool>(type: "bit", nullable: false),
                    RepositioningDistance = table.Column<double>(type: "float", nullable: false),
                    RotationAmount = table.Column<double>(type: "float", nullable: false),
                    Stress = table.Column<double>(type: "float", nullable: false),
                    StressMultiplier = table.Column<double>(type: "float", nullable: false),
                    SwingDiff = table.Column<double>(type: "float", nullable: false),
                    SwingFrequency = table.Column<double>(type: "float", nullable: false),
                    SwingSpeed = table.Column<double>(type: "float", nullable: false),
                    SwingTech = table.Column<double>(type: "float", nullable: false),
                    WallBuff = table.Column<double>(type: "float", nullable: false)
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
                name: "IX_MapSwingData_DifficultyStatisticsId",
                table: "MapSwingData",
                column: "DifficultyStatisticsId");
        }
    }
}
