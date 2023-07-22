using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeatLeader_Server.Migrations.ReadApp
{
    public partial class ReadContext : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountLinkRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OculusID = table.Column<int>(type: "int", nullable: false),
                    Random = table.Column<int>(type: "int", nullable: false),
                    IP = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountLinkRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SteamID = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OculusID = table.Column<int>(type: "int", nullable: false),
                    PCOculusID = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountLinks", x => x.Id);
                });

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
                name: "AuthIDs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Timestamp = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthIDs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthIPs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IP = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthIPs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Auths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Password = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Login = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Auths", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BannedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BanReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    Duration = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BeatSaverLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BeatSaverId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeatSaverLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeaderID = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Bio = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlayersCount = table.Column<int>(type: "int", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    AverageRank = table.Column<float>(type: "real", nullable: false),
                    AverageAccuracy = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CountryChanges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Timestamp = table.Column<int>(type: "int", nullable: false),
                    OldCountry = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewCountry = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cronTimestamps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HistoriesTimestamp = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cronTimestamps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomModes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomModes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventRankings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EndDate = table.Column<int>(type: "int", nullable: false),
                    PlaylistId = table.Column<int>(type: "int", nullable: false),
                    Image = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRankings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Friends",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friends", x => x.Id);
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
                name: "LoginAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Count = table.Column<int>(type: "int", nullable: false),
                    IP = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoginChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<int>(type: "int", nullable: false),
                    OldLogin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewLogin = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatreonFeatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Bio = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeftSaberColor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RightSaberColor = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatreonFeatures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatreonLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PatreonId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tier = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatreonLinks", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "RankQualification",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    RTMember = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CriteriaTimeset = table.Column<int>(type: "int", nullable: false),
                    CriteriaMet = table.Column<int>(type: "int", nullable: false),
                    CriteriaChecker = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CriteriaCommentary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MapperAllowed = table.Column<bool>(type: "bit", nullable: false),
                    MapperId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MapperQualification = table.Column<bool>(type: "bit", nullable: false),
                    ApprovalTimeset = table.Column<int>(type: "int", nullable: false),
                    Approved = table.Column<bool>(type: "bit", nullable: false),
                    Approvers = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankQualification", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReservedTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tag = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservedTags", x => x.Id);
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
                name: "ScoreImprovement",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timeset = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    BonusPp = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    AccRight = table.Column<float>(type: "real", nullable: false),
                    AccLeft = table.Column<float>(type: "real", nullable: false),
                    AverageRankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    TotalPp = table.Column<float>(type: "real", nullable: false),
                    TotalRank = table.Column<int>(type: "int", nullable: false),
                    BadCuts = table.Column<int>(type: "int", nullable: false),
                    MissedNotes = table.Column<int>(type: "int", nullable: false),
                    BombCuts = table.Column<int>(type: "int", nullable: false),
                    WallsHit = table.Column<int>(type: "int", nullable: false),
                    Pauses = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreImprovement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScoreRedirects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OldScoreId = table.Column<int>(type: "int", nullable: false),
                    NewScoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreRedirects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScoreRemovalLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Replay = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdminId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreRemovalLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Songs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Author = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Mapper = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MapperId = table.Column<int>(type: "int", nullable: false),
                    CoverImage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DownloadUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Bpm = table.Column<double>(type: "float", nullable: false),
                    Duration = table.Column<double>(type: "float", nullable: false),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedTime = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadTime = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Songs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TotalScore = table.Column<int>(type: "int", nullable: false),
                    TotalUnrankedScore = table.Column<int>(type: "int", nullable: false),
                    TotalRankedScore = table.Column<int>(type: "int", nullable: false),
                    LastScoreTime = table.Column<int>(type: "int", nullable: false),
                    LastUnrankedScoreTime = table.Column<int>(type: "int", nullable: false),
                    LastRankedScoreTime = table.Column<int>(type: "int", nullable: false),
                    AverageRankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    AverageUnrankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    AverageAccuracy = table.Column<float>(type: "real", nullable: false),
                    MedianRankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    MedianAccuracy = table.Column<float>(type: "real", nullable: false),
                    TopRankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    TopUnrankedAccuracy = table.Column<float>(type: "real", nullable: false),
                    TopAccuracy = table.Column<float>(type: "real", nullable: false),
                    TopPp = table.Column<float>(type: "real", nullable: false),
                    TopBonusPP = table.Column<float>(type: "real", nullable: false),
                    RankedPlayCount = table.Column<int>(type: "int", nullable: false),
                    UnrankedPlayCount = table.Column<int>(type: "int", nullable: false),
                    TotalPlayCount = table.Column<int>(type: "int", nullable: false),
                    AverageRankedRank = table.Column<float>(type: "real", nullable: false),
                    AverageUnrankedRank = table.Column<float>(type: "real", nullable: false),
                    AverageRank = table.Column<float>(type: "real", nullable: false),
                    SSPPlays = table.Column<int>(type: "int", nullable: false),
                    SSPlays = table.Column<int>(type: "int", nullable: false),
                    SPPlays = table.Column<int>(type: "int", nullable: false),
                    SPlays = table.Column<int>(type: "int", nullable: false),
                    APlays = table.Column<int>(type: "int", nullable: false),
                    TopPlatform = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TopHMD = table.Column<int>(type: "int", nullable: false),
                    DailyImprovements = table.Column<int>(type: "int", nullable: false),
                    ReplaysWatched = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StatsHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Pp = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rank = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CountryRank = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalScore = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AverageRankedAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TopAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TopPp = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AverageAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MedianAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MedianRankedAccuracy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalPlayCount = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RankedPlayCount = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReplaysWatched = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatsHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TwitchLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TwitchId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TwitterLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TwitterId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitterLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WinTracker",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Won = table.Column<bool>(type: "bit", nullable: false),
                    EndTime = table.Column<float>(type: "real", nullable: false),
                    NbOfPause = table.Column<int>(type: "int", nullable: false),
                    JumpDistance = table.Column<float>(type: "real", nullable: false),
                    TotalScore = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WinTracker", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualificationChange",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timeset = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldRankability = table.Column<float>(type: "real", nullable: false),
                    OldStars = table.Column<float>(type: "real", nullable: false),
                    OldType = table.Column<int>(type: "int", nullable: false),
                    OldCriteriaMet = table.Column<int>(type: "int", nullable: false),
                    NewRankability = table.Column<float>(type: "real", nullable: false),
                    NewStars = table.Column<float>(type: "real", nullable: false),
                    NewType = table.Column<int>(type: "int", nullable: false),
                    NewCriteriaMet = table.Column<int>(type: "int", nullable: false),
                    RankQualificationId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualificationChange", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualificationChange_RankQualification_RankQualificationId",
                        column: x => x.RankQualificationId,
                        principalTable: "RankQualification",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DifficultyDescription",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Value = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    DifficultyName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModeName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Nominated = table.Column<bool>(type: "bit", nullable: false),
                    NominatedTime = table.Column<int>(type: "int", nullable: false),
                    Qualified = table.Column<bool>(type: "bit", nullable: false),
                    QualifiedTime = table.Column<int>(type: "int", nullable: false),
                    Ranked = table.Column<bool>(type: "bit", nullable: false),
                    RankedTime = table.Column<int>(type: "int", nullable: false),
                    Stars = table.Column<float>(type: "real", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Njs = table.Column<float>(type: "real", nullable: false),
                    Nps = table.Column<float>(type: "real", nullable: false),
                    Notes = table.Column<int>(type: "int", nullable: false),
                    Bombs = table.Column<int>(type: "int", nullable: false),
                    Walls = table.Column<int>(type: "int", nullable: false),
                    MaxScore = table.Column<int>(type: "int", nullable: false),
                    SongId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DifficultyDescription", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DifficultyDescription_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Avatar = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Histories = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MapperId = table.Column<int>(type: "int", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    Banned = table.Column<bool>(type: "bit", nullable: false),
                    Inactive = table.Column<bool>(type: "bit", nullable: false),
                    ExternalProfileUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastTwoWeeksTime = table.Column<float>(type: "real", nullable: false),
                    AllTime = table.Column<float>(type: "real", nullable: false),
                    ScoreStatsId = table.Column<int>(type: "int", nullable: false),
                    StatsHistoryId = table.Column<int>(type: "int", nullable: true),
                    PatreonFeaturesId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_PatreonFeatures_PatreonFeaturesId",
                        column: x => x.PatreonFeaturesId,
                        principalTable: "PatreonFeatures",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Players_Stats_ScoreStatsId",
                        column: x => x.ScoreStatsId,
                        principalTable: "Stats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Players_StatsHistory_StatsHistoryId",
                        column: x => x.StatsHistoryId,
                        principalTable: "StatsHistory",
                        principalColumn: "Id");
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

            migrationBuilder.CreateTable(
                name: "Leaderboards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SongId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DifficultyId = table.Column<int>(type: "int", nullable: false),
                    QualificationId = table.Column<int>(type: "int", nullable: true),
                    Plays = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leaderboards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leaderboards_DifficultyDescription_DifficultyId",
                        column: x => x.DifficultyId,
                        principalTable: "DifficultyDescription",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Leaderboards_RankQualification_QualificationId",
                        column: x => x.QualificationId,
                        principalTable: "RankQualification",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Leaderboards_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Badges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Image = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Badges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Badges_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClanPlayer",
                columns: table => new
                {
                    ClansId = table.Column<int>(type: "int", nullable: false),
                    PlayersId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanPlayer", x => new { x.ClansId, x.PlayersId });
                    table.ForeignKey(
                        name: "FK_ClanPlayer_Clans_ClansId",
                        column: x => x.ClansId,
                        principalTable: "Clans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClanPlayer_Players_PlayersId",
                        column: x => x.PlayersId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventPlayer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    EventRankingId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventPlayer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventPlayer_EventRankings_EventRankingId",
                        column: x => x.EventRankingId,
                        principalTable: "EventRankings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventPlayer_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerPlayerFriends",
                columns: table => new
                {
                    FriendsId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PlayerFriendsId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerPlayerFriends", x => new { x.FriendsId, x.PlayerFriendsId });
                    table.ForeignKey(
                        name: "FK_PlayerPlayerFriends_Friends_PlayerFriendsId",
                        column: x => x.PlayerFriendsId,
                        principalTable: "Friends",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerPlayerFriends_Players_FriendsId",
                        column: x => x.FriendsId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSocial",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Service = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Link = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    User = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSocial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerSocial_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventRankingLeaderboard",
                columns: table => new
                {
                    EventsId = table.Column<int>(type: "int", nullable: false),
                    LeaderboardsId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRankingLeaderboard", x => new { x.EventsId, x.LeaderboardsId });
                    table.ForeignKey(
                        name: "FK_EventRankingLeaderboard_EventRankings_EventsId",
                        column: x => x.EventsId,
                        principalTable: "EventRankings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventRankingLeaderboard_Leaderboards_LeaderboardsId",
                        column: x => x.LeaderboardsId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FailedScores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaseScore = table.Column<int>(type: "int", nullable: false),
                    ModifiedScore = table.Column<int>(type: "int", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    Weight = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    Replay = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Modifiers = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BadCuts = table.Column<int>(type: "int", nullable: false),
                    MissedNotes = table.Column<int>(type: "int", nullable: false),
                    BombCuts = table.Column<int>(type: "int", nullable: false),
                    WallsHit = table.Column<int>(type: "int", nullable: false),
                    Pauses = table.Column<int>(type: "int", nullable: false),
                    FullCombo = table.Column<bool>(type: "bit", nullable: false),
                    Hmd = table.Column<int>(type: "int", nullable: false),
                    Timeset = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailedScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FailedScores_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FailedScores_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeaderboardStatistics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Relevant = table.Column<bool>(type: "bit", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    HitTrackerId = table.Column<int>(type: "int", nullable: false),
                    AccuracyTrackerId = table.Column<int>(type: "int", nullable: false),
                    WinTrackerId = table.Column<int>(type: "int", nullable: false),
                    ScoreGraphTrackerId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardStatistics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaderboardStatistics_AccuracyTracker_AccuracyTrackerId",
                        column: x => x.AccuracyTrackerId,
                        principalTable: "AccuracyTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaderboardStatistics_HitTracker_HitTrackerId",
                        column: x => x.HitTrackerId,
                        principalTable: "HitTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaderboardStatistics_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaderboardStatistics_ScoreGraphTracker_ScoreGraphTrackerId",
                        column: x => x.ScoreGraphTrackerId,
                        principalTable: "ScoreGraphTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaderboardStatistics_WinTracker_WinTrackerId",
                        column: x => x.WinTrackerId,
                        principalTable: "WinTracker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Scores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaseScore = table.Column<int>(type: "int", nullable: false),
                    ModifiedScore = table.Column<int>(type: "int", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Pp = table.Column<float>(type: "real", nullable: false),
                    BonusPp = table.Column<float>(type: "real", nullable: false),
                    Qualification = table.Column<bool>(type: "bit", nullable: false),
                    Weight = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CountryRank = table.Column<int>(type: "int", nullable: false),
                    Replay = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Modifiers = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BadCuts = table.Column<int>(type: "int", nullable: false),
                    MissedNotes = table.Column<int>(type: "int", nullable: false),
                    BombCuts = table.Column<int>(type: "int", nullable: false),
                    WallsHit = table.Column<int>(type: "int", nullable: false),
                    Pauses = table.Column<int>(type: "int", nullable: false),
                    FullCombo = table.Column<bool>(type: "bit", nullable: false),
                    Hmd = table.Column<int>(type: "int", nullable: false),
                    AccRight = table.Column<float>(type: "real", nullable: false),
                    AccLeft = table.Column<float>(type: "real", nullable: false),
                    Timeset = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timepost = table.Column<int>(type: "int", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeaderboardId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ScoreImprovementId = table.Column<int>(type: "int", nullable: true),
                    Banned = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scores_Leaderboards_LeaderboardId",
                        column: x => x.LeaderboardId,
                        principalTable: "Leaderboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Scores_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Scores_ScoreImprovement_ScoreImprovementId",
                        column: x => x.ScoreImprovementId,
                        principalTable: "ScoreImprovement",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClanUser",
                columns: table => new
                {
                    BannedClansId = table.Column<int>(type: "int", nullable: false),
                    BannedId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanUser", x => new { x.BannedClansId, x.BannedId });
                    table.ForeignKey(
                        name: "FK_ClanUser_Clans_BannedClansId",
                        column: x => x.BannedClansId,
                        principalTable: "Clans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClanUser_Users_BannedId",
                        column: x => x.BannedId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClanUser1",
                columns: table => new
                {
                    ClanRequestId = table.Column<int>(type: "int", nullable: false),
                    RequestsId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanUser1", x => new { x.ClanRequestId, x.RequestsId });
                    table.ForeignKey(
                        name: "FK_ClanUser1_Clans_ClanRequestId",
                        column: x => x.ClanRequestId,
                        principalTable: "Clans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClanUser1_Users_RequestsId",
                        column: x => x.RequestsId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsShared = table.Column<bool>(type: "bit", nullable: false),
                    Link = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Playlists_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RankVotings",
                columns: table => new
                {
                    ScoreId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Diff = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rankability = table.Column<float>(type: "real", nullable: false),
                    Stars = table.Column<float>(type: "real", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Timeset = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankVotings", x => x.ScoreId);
                    table.ForeignKey(
                        name: "FK_RankVotings_Scores_ScoreId",
                        column: x => x.ScoreId,
                        principalTable: "Scores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VoterFeedback",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RTMember = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Value = table.Column<float>(type: "real", nullable: false),
                    RankVotingScoreId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoterFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoterFeedback_RankVotings_RankVotingScoreId",
                        column: x => x.RankVotingScoreId,
                        principalTable: "RankVotings",
                        principalColumn: "ScoreId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Badges_PlayerId",
                table: "Badges",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanPlayer_PlayersId",
                table: "ClanPlayer",
                column: "PlayersId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanUser_BannedId",
                table: "ClanUser",
                column: "BannedId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanUser1_RequestsId",
                table: "ClanUser1",
                column: "RequestsId");

            migrationBuilder.CreateIndex(
                name: "IX_DifficultyDescription_SongId",
                table: "DifficultyDescription",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_EventPlayer_EventRankingId",
                table: "EventPlayer",
                column: "EventRankingId");

            migrationBuilder.CreateIndex(
                name: "IX_EventPlayer_PlayerId",
                table: "EventPlayer",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_EventRankingLeaderboard_LeaderboardsId",
                table: "EventRankingLeaderboard",
                column: "LeaderboardsId");

            migrationBuilder.CreateIndex(
                name: "IX_FailedScores_LeaderboardId",
                table: "FailedScores",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_FailedScores_PlayerId",
                table: "FailedScores",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Leaderboards_DifficultyId",
                table: "Leaderboards",
                column: "DifficultyId");

            migrationBuilder.CreateIndex(
                name: "IX_Leaderboards_QualificationId",
                table: "Leaderboards",
                column: "QualificationId");

            migrationBuilder.CreateIndex(
                name: "IX_Leaderboards_SongId",
                table: "Leaderboards",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStatistics_AccuracyTrackerId",
                table: "LeaderboardStatistics",
                column: "AccuracyTrackerId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStatistics_HitTrackerId",
                table: "LeaderboardStatistics",
                column: "HitTrackerId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStatistics_LeaderboardId",
                table: "LeaderboardStatistics",
                column: "LeaderboardId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStatistics_ScoreGraphTrackerId",
                table: "LeaderboardStatistics",
                column: "ScoreGraphTrackerId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardStatistics_WinTrackerId",
                table: "LeaderboardStatistics",
                column: "WinTrackerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerPlayerFriends_PlayerFriendsId",
                table: "PlayerPlayerFriends",
                column: "PlayerFriendsId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_PatreonFeaturesId",
                table: "Players",
                column: "PatreonFeaturesId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_ScoreStatsId",
                table: "Players",
                column: "ScoreStatsId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_StatsHistoryId",
                table: "Players",
                column: "StatsHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSocial_PlayerId",
                table: "PlayerSocial",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_UserId",
                table: "Playlists",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_QualificationChange_RankQualificationId",
                table: "QualificationChange",
                column: "RankQualificationId");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_LeaderboardId",
                table: "Scores",
                column: "LeaderboardId");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_PlayerId",
                table: "Scores",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_ScoreImprovementId",
                table: "Scores",
                column: "ScoreImprovementId");

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

            migrationBuilder.CreateIndex(
                name: "IX_Songs_Hash",
                table: "Songs",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_PlayerId",
                table: "Users",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_VoterFeedback_RankVotingScoreId",
                table: "VoterFeedback",
                column: "RankVotingScoreId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountLinkRequests");

            migrationBuilder.DropTable(
                name: "AccountLinks");

            migrationBuilder.DropTable(
                name: "AuthIDs");

            migrationBuilder.DropTable(
                name: "AuthIPs");

            migrationBuilder.DropTable(
                name: "Auths");

            migrationBuilder.DropTable(
                name: "Badges");

            migrationBuilder.DropTable(
                name: "Bans");

            migrationBuilder.DropTable(
                name: "BeatSaverLinks");

            migrationBuilder.DropTable(
                name: "ClanPlayer");

            migrationBuilder.DropTable(
                name: "ClanUser");

            migrationBuilder.DropTable(
                name: "ClanUser1");

            migrationBuilder.DropTable(
                name: "CountryChanges");

            migrationBuilder.DropTable(
                name: "cronTimestamps");

            migrationBuilder.DropTable(
                name: "CustomModes");

            migrationBuilder.DropTable(
                name: "EventPlayer");

            migrationBuilder.DropTable(
                name: "EventRankingLeaderboard");

            migrationBuilder.DropTable(
                name: "FailedScores");

            migrationBuilder.DropTable(
                name: "LeaderboardStatistics");

            migrationBuilder.DropTable(
                name: "LoginAttempts");

            migrationBuilder.DropTable(
                name: "LoginChanges");

            migrationBuilder.DropTable(
                name: "PatreonLinks");

            migrationBuilder.DropTable(
                name: "PlayerPlayerFriends");

            migrationBuilder.DropTable(
                name: "PlayerSocial");

            migrationBuilder.DropTable(
                name: "Playlists");

            migrationBuilder.DropTable(
                name: "QualificationChange");

            migrationBuilder.DropTable(
                name: "RankChanges");

            migrationBuilder.DropTable(
                name: "ReservedTags");

            migrationBuilder.DropTable(
                name: "ScoreRedirects");

            migrationBuilder.DropTable(
                name: "ScoreRemovalLogs");

            migrationBuilder.DropTable(
                name: "ScoreStatistics");

            migrationBuilder.DropTable(
                name: "TwitchLinks");

            migrationBuilder.DropTable(
                name: "TwitterLinks");

            migrationBuilder.DropTable(
                name: "VoterFeedback");

            migrationBuilder.DropTable(
                name: "Clans");

            migrationBuilder.DropTable(
                name: "EventRankings");

            migrationBuilder.DropTable(
                name: "Friends");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "AccuracyTracker");

            migrationBuilder.DropTable(
                name: "HitTracker");

            migrationBuilder.DropTable(
                name: "ScoreGraphTracker");

            migrationBuilder.DropTable(
                name: "WinTracker");

            migrationBuilder.DropTable(
                name: "RankVotings");

            migrationBuilder.DropTable(
                name: "Scores");

            migrationBuilder.DropTable(
                name: "Leaderboards");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "ScoreImprovement");

            migrationBuilder.DropTable(
                name: "DifficultyDescription");

            migrationBuilder.DropTable(
                name: "RankQualification");

            migrationBuilder.DropTable(
                name: "PatreonFeatures");

            migrationBuilder.DropTable(
                name: "Stats");

            migrationBuilder.DropTable(
                name: "StatsHistory");

            migrationBuilder.DropTable(
                name: "Songs");
        }
    }
}
