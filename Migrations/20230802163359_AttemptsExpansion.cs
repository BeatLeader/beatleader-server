using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    /// <inheritdoc />
    public partial class AttemptsExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AccLeft",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "AccPP",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "AccRight",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "Accuracy",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "AnonimusReplayWatched",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AuthorizedReplayWatched",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BadCuts",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BaseScore",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BombCuts",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "BonusPp",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "Controller",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "PlayerLeaderboardStats",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CountryRank",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "FcAccuracy",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "FcPp",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<bool>(
                name: "FullCombo",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Hmd",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "LeftTiming",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "MaxCombo",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxStreak",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MissedNotes",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ModifiedScore",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Modifiers",
                table: "PlayerLeaderboardStats",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "PassPP",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "Pauses",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "PlayerLeaderboardStats",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<float>(
                name: "Pp",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Qualification",
                table: "PlayerLeaderboardStats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Rank",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReplayOffsetsId",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "RightTiming",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "ScoreId",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoreImprovementId",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "TechPP",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "Timepost",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WallsHit",
                table: "PlayerLeaderboardStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "Weight",
                table: "PlayerLeaderboardStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_ReplayOffsetsId",
                table: "PlayerLeaderboardStats",
                column: "ReplayOffsetsId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLeaderboardStats_ScoreImprovementId",
                table: "PlayerLeaderboardStats",
                column: "ScoreImprovementId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerLeaderboardStats_ReplayOffsets_ReplayOffsetsId",
                table: "PlayerLeaderboardStats",
                column: "ReplayOffsetsId",
                principalTable: "ReplayOffsets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerLeaderboardStats_ScoreImprovement_ScoreImprovementId",
                table: "PlayerLeaderboardStats",
                column: "ScoreImprovementId",
                principalTable: "ScoreImprovement",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerLeaderboardStats_ReplayOffsets_ReplayOffsetsId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropForeignKey(
                name: "FK_PlayerLeaderboardStats_ScoreImprovement_ScoreImprovementId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropIndex(
                name: "IX_PlayerLeaderboardStats_ReplayOffsetsId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropIndex(
                name: "IX_PlayerLeaderboardStats_ScoreImprovementId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "AccLeft",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "AccPP",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "AccRight",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Accuracy",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "AnonimusReplayWatched",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "AuthorizedReplayWatched",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "BadCuts",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "BaseScore",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "BombCuts",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "BonusPp",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Controller",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "CountryRank",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "FcAccuracy",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "FcPp",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "FullCombo",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Hmd",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "LeftTiming",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "MaxCombo",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "MaxStreak",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "MissedNotes",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "ModifiedScore",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Modifiers",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "PassPP",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Pauses",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Pp",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Qualification",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Rank",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "ReplayOffsetsId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "RightTiming",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "ScoreId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "ScoreImprovementId",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "TechPP",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Timepost",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "WallsHit",
                table: "PlayerLeaderboardStats");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "PlayerLeaderboardStats");
        }
    }
}
