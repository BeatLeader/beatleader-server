using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.Storage
{
    /// <inheritdoc />
    public partial class MapSwingData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    DifficultyStatisticsId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapSwingData", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MapSwingData_DifficultyStatisticsId",
                table: "MapSwingData",
                column: "DifficultyStatisticsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapSwingData");
        }
    }
}
