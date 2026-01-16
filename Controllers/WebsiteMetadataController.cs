using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Ganss.Xss;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Text;
using System.Threading.Tasks;
using BeatLeader_Server.Enums;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class WebsiteMetadatController : Controller {

        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        CurrentUserController _userController;
        PlayerController _playerController;
        IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;

        public WebsiteMetadatController(
            AppContext context,
            IWebHostEnvironment env,
            CurrentUserController userController,
            PlayerController playerController,
            IConfiguration configuration)
        {
            _context = context;
            _userController = userController;
            _playerController = playerController;
            _environment = env;
            _s3Client = configuration.GetS3Client();
            _configuration = configuration;
        }

        private const string SITE_NAME = "BeatLeader";
        private const string TWITTER_HANDLE = "@handle";
        private const string TWITTER_SITE = "@beatleader_";
        private const string BASE_URL = "https://beatleader.com";
        private const string DEFAULT_IMAGE = "/assets/logo-small.png";

        [HttpGet("~/metadata/")]
        public IActionResult GetLandingPageMeta()
        {
            var title = $"{SITE_NAME} - Beat Saber leaderboard";
            var description = $"{SITE_NAME} is Beat Saber's leaderboard with open code and community. Start posting your scores to compete with others on more than 100,000 different maps.";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}{DEFAULT_IMAGE}",
                imageAlt: $"{SITE_NAME}'s logo",
                siteName: SITE_NAME
            ), "text/html");
        }

        [HttpGet("~/metadata/maps")]
        public IActionResult GetMapsPortalMeta()
        {
            var title = $"{SITE_NAME} - Maps";
            var description = "Discover custom maps for Beat Saber: trending, ranked and featured by the community";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}{DEFAULT_IMAGE}",
                imageAlt: $"{SITE_NAME}'s logo",
                siteName: SITE_NAME
            ), "text/html");
        }

        [HttpGet("~/metadata/maps/{type}/{*rest}")]
        public IActionResult GetMapsListMeta(string type = "ranked", [FromQuery] string search = "", 
            [FromQuery] double? starsFrom = null, [FromQuery] double? starsTo = null, string? rest = null)
        {
            var title = GenerateMapsListTitle(type, search, starsFrom, starsTo);
            var description = GenerateMapsListDescription(type, search, starsFrom, starsTo);
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}{DEFAULT_IMAGE}",
                imageAlt: $"{SITE_NAME}'s logo",
                siteName: $"{SITE_NAME} - Maps"
            ), "text/html");
        }

        [HttpGet("~/metadata/events/{*rest}")]
        public IActionResult GetEventsMeta(string? rest = null)
        {
            var title = "Beat Saber events";
            var description = "Competitions, ranked weeks and special occasions";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}{DEFAULT_IMAGE}",
                imageAlt: $"{SITE_NAME}'s logo",
                siteName: SITE_NAME
            ), "text/html");
        }

        [HttpGet("~/metadata/leaderboards/{*rest}")]
        public IActionResult GetLeaderboardsMeta([FromQuery] string type = "ranked", string? rest = null)
        {
            var title = $"{SITE_NAME} - Leaderboards";
            var description = type == "ranked" 
                ? "List of Beat Saber ranked maps" 
                : "Search for leaderboards of Beat Saber maps";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}{DEFAULT_IMAGE}",
                imageAlt: $"{SITE_NAME}'s logo",
                siteName: SITE_NAME
            ), "text/html");
        }

        [HttpGet("~/metadata/u/{playerId}/{*rest}")]
        public async Task<ActionResult> GetPlayerMeta(string playerId, string? rest = null, [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
        {
            playerId = await _context.PlayerIdToMain(playerId);
            
            PlayerData player;
            
            if (leaderboardContext == LeaderboardContexts.None || leaderboardContext == LeaderboardContexts.General) {
                player = await _context.Players.AsNoTracking().Where(p => p.Id == playerId).Select(p =>
                new PlayerData
                {
                    Name = p.Name,
                    Rank = p.Rank,
                    CountryRank = p.CountryRank,
                    CountryName = p.Country,
                    PP = p.Pp,
                    AverageRankedAccuracy = p.ScoreStats.AverageRankedAccuracy,
                    Avatar = p.Avatar
                }).FirstOrDefaultAsync();
            } else {
                player = await _context.PlayerContextExtensions.AsNoTracking().Where(p => p.PlayerId == playerId && p.Context == leaderboardContext).Select(p =>
                new PlayerData
                {
                    Name = p.Name,
                    Rank = p.Rank,
                    CountryRank = p.CountryRank,
                    CountryName = p.Country,
                    PP = p.Pp,
                    AverageRankedAccuracy = p.ScoreStats.AverageRankedAccuracy,
                    Avatar = p.PlayerInstance.Avatar
                }).FirstOrDefaultAsync();
            }
            
            if (player == null)
                return NotFound();

            var title = player.Name;
            var description = $@"
                Top #{player.Rank:N0} global🌐/#{player.CountryRank:N0} {GetCountryName(player.CountryName)}
                {player.PP:N0}pp 
                {Math.Round(player.AverageRankedAccuracy * 100.0, 2)}% average accuracy";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: player.Avatar,
                imageAlt: $"{player.Name} profile picture",
                siteName: $"Player Profile - {SITE_NAME}{(leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None ? " - " + leaderboardContext : "")}"
            ), "text/html");
        }

        [HttpGet("~/metadata/leaderboard/{leaderboardId}/{*rest}")]
        [HttpGet("~/metadata/leaderboard/{leaderboardType}/{leaderboardId}/{*rest}")]
        public async Task<IActionResult> GetLeaderboardMeta(string leaderboardId, string? leaderboardType = null, string? rest = null, [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
        {
            var leaderboard = await _context.Leaderboards.Where(l => l.Id == leaderboardId).Select(l =>

            new LeaderboardData
            {
                SongName = l.Song.Name,
                DifficultyName = l.Difficulty.DifficultyName,
                ModeName = l.Difficulty.ModeName,
                Author = l.Song.Author,
                Mapper = l.Song.Mapper,
                Status = l.Difficulty.Status.ToString(),
                PassRating = l.Difficulty.PassRating,
                AccRating = l.Difficulty.AccRating,
                TechRating = l.Difficulty.TechRating,
                ImageUrl = l.Song.CoverImage
            }).FirstOrDefaultAsync();
            
            if (leaderboard == null)
                return NotFound();

            var title = $"{leaderboard.SongName} | {(leaderboard.DifficultyName == "ExpertPlus" ? "Expert+" : leaderboard.DifficultyName)} | {leaderboard.ModeName}";
            var descriptionBuilder = new StringBuilder();
            descriptionBuilder.AppendLine($"Author: {leaderboard.Author}");
            descriptionBuilder.AppendLine($"Mapped by: {leaderboard.Mapper}");
            descriptionBuilder.AppendLine($"Status: {leaderboard.Status}");
            
            if (leaderboard.Status == "inevent" || leaderboard.Status == "OST" || leaderboard.Status == "ranked" || leaderboard.Status == "qualified" || leaderboard.Status == "nominated") {
                if (leaderboard.PassRating.HasValue)
                    descriptionBuilder.Append($"Pass: {leaderboard.PassRating.Value:F2}★ ");
                if (leaderboard.AccRating.HasValue)
                    descriptionBuilder.Append($"Acc: {leaderboard.AccRating.Value:F2}★ ");
                if (leaderboard.TechRating.HasValue)
                    descriptionBuilder.Append($"Tech: {leaderboard.TechRating.Value:F2}★");
            }
            
            return Content(GenerateMetaTags(
                title: title,
                description: descriptionBuilder.ToString(),
                imageUrl: leaderboard.ImageUrl,
                imageAlt: $"{leaderboard.SongName} cover",
                siteName: $"Map - {SITE_NAME}{(leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None ? " - " + leaderboardContext : "")}"
            ), "text/html");
        }

        [HttpGet("~/metadata/score/{scoreId}")]
        public async Task<IActionResult> GetScoreMeta(int scoreId)
        {
            var score = await _context.Scores.AsNoTracking().Where(s => s.Id == scoreId).Select(s =>
            new ScoreData
            {
                PlayerName = s.Player.Name,
                SongName = s.Leaderboard.Song.Name,
                Author = s.Leaderboard.Song.Author,
                Rank = s.Rank,
                Accuracy = s.Accuracy,
                PP = s.Pp,
                Mods = s.Modifiers,
                DifficultyName = s.Leaderboard.Difficulty.DifficultyName,
                Timepost = s.Timepost,
                SongImageUrl = s.Leaderboard.Song.CoverImage
            }).FirstOrDefaultAsync();
            
            if (score == null)
                return NotFound();

            var title = $"{score.PlayerName} on {score.SongName} by {score.Author}";
            var description = $@"
                #{score.Rank} with {Math.Round(score.Accuracy * 100.0, 2)}%{(score.PP > 0 ? $" and {Math.Round(score.PP, 2)}pp" : "")}{(score.Mods?.Length > 0 ? $"({score.Mods})" : "")}
                {(score.DifficultyName == "ExpertPlus" ? "Expert+" : score.DifficultyName)}
                submitted {GetRelativeTime(score.Timepost)}";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: score.SongImageUrl,
                imageAlt: $"{score.SongName} cover",
                siteName: $"Score - {SITE_NAME}"
            ), "text/html");
        }

        [HttpGet("~/metadata/clan/{tag}/{*rest}")]
        public async Task<IActionResult> GetClanMeta(string tag, string? rest = null)
        {
            var clan = await _context.Clans.AsNoTracking().Where(c => c.Tag == tag).Select(c =>
            new ClanData
            {
                Name = c.Name,
                Tag = c.Tag,
                Description = c.Description,
                PlayersCount = c.PlayersCount,
                Icon = c.Icon,
                Color = c.Color
            }).FirstOrDefaultAsync();
            
            if (clan == null)
                return NotFound();

            var title = clan.Name;
            var description = $@"
                [{clan.Tag}]
                {clan.Description}
                {clan.PlayersCount} player{(clan.PlayersCount > 1 ? "s" : "")}";
            
            var html = new StringBuilder();
            html.AppendLine(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: clan.Icon,
                imageAlt: $"{clan.Name} cover",
                siteName: $"Clan - {SITE_NAME}",
                themeColor: clan.Color
            ));
            
            
            return Content(html.ToString(), "text/html");
        }

        [HttpGet("~/metadata/event/{eventId}/{*rest}")]
        public async Task<IActionResult> GetEventMeta(string eventId, string? rest = null)
        {
            int? numericalId = null;
            if (int.TryParse(eventId, out int parsedId)) {
                numericalId = parsedId;
            }

            var eventData = await _context.EventRankings.Where(e => numericalId != null ? e.Id == (int)numericalId : e.PageAlias == eventId).Select(e => new EventData
            {
                Name = e.Name,
                EndDate = e.EndDate,
                Image = e.Image
            }).FirstOrDefaultAsync();
            
            if (eventData == null)
                return NotFound();

            var title = eventData.Name;
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var description = currentTime < eventData.EndDate 
                ? $"Beat Saber competition.\nWill end in {GetRelativeTimeFuture(eventData.EndDate)}" 
                : $"Beat Saber competition.\nEnded {GetRelativeTime(eventData.EndDate)}";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: eventData.Image,
                imageAlt: $"{eventData.Name} event icon",
                siteName: $"Event - {SITE_NAME}"
            ), "text/html");
        }

        [HttpGet("~/metadata/ranking/{*rest}")]
        public async Task<IActionResult> GetRankingMeta(
            [FromQuery] PlayerSortBy sortBy = PlayerSortBy.Pp,
            [FromQuery] int page = 1,
            [FromQuery] int count = 50,
            [FromQuery] string search = "",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string countries = "",
            [FromQuery] MapsType mapsType = MapsType.Ranked,
            [FromQuery] PpType ppType = PpType.General,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] bool friends = false,
            [FromQuery] string? pp_range = null,
            [FromQuery] string? acc_pp_range = null,
            [FromQuery] string? pass_pp_range = null,
            [FromQuery] string? tech_pp_range = null,
            [FromQuery] string? score_range = null,
            [FromQuery] string? ranked_score_range = null,
            [FromQuery] string? platform = null,
            [FromQuery] string? role = null,
            [FromQuery] string? hmd = null,
            [FromQuery] int? activityPeriod = null,
            [FromQuery] int? firstScoreTime = null,
            [FromQuery] int? recentScoreTime = null,
            [FromQuery] MapperStatus? mapperStatus = null,
            [FromQuery] bool? banned = null, 
            string? rest = null)
        {
            var title = !string.IsNullOrEmpty(countries) 
                ? $"Player ranking in {GetCountryNameFromCodes(countries)}" 
                : "Global player ranking";
            
            var players = await _playerController.GetPlayers(sortBy, page, count, search, order, countries, mapsType, ppType, leaderboardContext, friends, pp_range, acc_pp_range, pass_pp_range, tech_pp_range, score_range, ranked_score_range, platform, role, hmd, activityPeriod, firstScoreTime, recentScoreTime, mapperStatus, banned);
            
            var description = GenerateRankingDescription(players.Value);
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}{DEFAULT_IMAGE}",
                imageAlt: "Ranking logo",
                siteName: $"{SITE_NAME}{(leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None ? " - " + leaderboardContext : "")}"
            ), "text/html");
        }

        [HttpGet("~/metadata/tibytes-presets")]
        public IActionResult GetTibytesPresetsMeta()
        {
            var title = "Tibytes Presets";
            var description = "Download Tibytes Presets - A special version of ReeSabers with legendary presets included. Exclusive to BeatLeader Patreon supporters.";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}/assets/tibytes-preset.webp",
                imageAlt: "Tibytes Presets",
                siteName: SITE_NAME
            ), "text/html");
        }

        [HttpGet("~/metadata/census2023")]
        public IActionResult GetCensusMeta()
        {
            var title = "Beat Saber in Numbers";
            var description = "2023 Player Census results";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}/assets/census2023.webp",
                imageAlt: description,
                siteName: SITE_NAME,
                twitterCardType: "summary_large_image"
            ), "text/html");
        }

        [HttpGet("~/metadata/badges")]
        public IActionResult GetBadgesMeta()
        {
            var title = "Beat Saber Badges";
            var description = "A list of all badges that were received on BeatLeader.";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}{DEFAULT_IMAGE}",
                imageAlt: $"{SITE_NAME}'s logo",
                siteName: $"Badges - {SITE_NAME}"
            ), "text/html");
        }

        [HttpGet("~/metadata/playlists")]
        [HttpGet("~/metadata/playlists/featured/{*rest}")]
        public IActionResult GetFeaturedPlaylistsMeta(string? rest = null)
        {
            var title = $"Beat Saber playlists";
            var description = "Find lists of ranked, qualified or features maps to play";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}/assets/defaultplaylisticon.png",
                imageAlt: $"{title} picture",
                siteName: $"Playlists - {SITE_NAME}"
            ), "text/html");
        }

        [HttpGet("~/metadata/replayed-landing")]
        public IActionResult GetReplayedLandingMeta()
        {
            var title = "BeatLeader rePlayed 2024";
            var description = "Discover your Beat Saber year in review - top songs, mappers, and achievements.";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}/assets/replayed2024.webp",
                imageAlt: "BeatLeader rePlayed 2024",
                siteName: SITE_NAME
            ), "text/html");
        }

        [HttpGet("~/metadata/project-tree")]
        public IActionResult GetProjectTreeMeta()
        {
            var title = "Project Tree";
            var description = "Daily mapping challenge - December 19th through December 31st. Compete on a new map each day!";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}/assets/projecttree.webp",
                imageAlt: "Project Tree event",
                siteName: SITE_NAME
            ), "text/html");
        }

        [HttpGet("~/metadata/clans-map")]
        public IActionResult GetClansMapMeta()
        {
            var title = "Clans War Global Map";
            var description = "Global map of ranked songs conquest by clans!";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: "https://cdn.assets.beatleader.com/clansmap-change-daily-latest.gif",
                imageAlt: description,
                siteName: SITE_NAME,
                twitterCardType: "summary_large_image"
            ), "text/html");
        }

        [HttpGet("~/metadata/building-blocks-2024")]
        public IActionResult GetBuildingBlocks2024Meta()
        {
            var title = "Building Blocks 2024";
            var description = "Ongoing mapping contest with a prize pool and chance to get directly into ranked!";
            
            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"{BASE_URL}/assets/buildingblockslogo.png",
                imageAlt: "Building Blocks 2024 Logo",
                siteName: SITE_NAME
            ), "text/html");
        }

        [NonAction]
        private async Task<dynamic?> DownloadPlaylist(string id) {
            var stream = await _s3Client.DownloadPlaylist(id + ".bplist");
            
            if (stream != null) {
                using (var objectStream = new MemoryStream(5))
                {
                    var outputStream = new MemoryStream(5);
                    stream.CopyTo(objectStream);
                    objectStream.Position = 0;
                    objectStream.CopyTo(outputStream);
                    objectStream.Position = 0;
                    outputStream.Position = 0;

                    return objectStream.ObjectFromStream();
                }
                
            } else {
                return null;
            }
        }

        [HttpGet("~/metadata/playlist/{playlistId}")]
        public async Task<ActionResult> GetPlaylistMeta(string playlistId)
        {
            playlistId = playlistId.Replace(".bplist", "");
            if (int.TryParse(playlistId, out var intId)) {
                string? currentID = HttpContext.CurrentUserID(_context);

                if (intId != 33) {
                    var playlistRecord = await _context.Playlists.FirstOrDefaultAsync(p => p.Id == intId);
                    if (playlistRecord == null) {
                        return NotFound();
                    } else if (playlistRecord.OwnerId != currentID && !playlistRecord.IsShared) {
                        return Unauthorized("");
                    }
                }
            }

            var playlist = await DownloadPlaylist(playlistId);

            if (playlist == null)
                return NotFound();

            var title = playlist.playlistTitle;
            var description = $@"
                Collection of Beat Saber maps
                {playlist.songs.Count} songs
                {(ExpandantoObject.HasProperty(playlist, "playlistDescription") ? playlist.playlistDescription : "")}";

            return Content(GenerateMetaTags(
                title: title,
                description: description,
                imageUrl: $"https://api.beatleader.com/playlist/image/{playlistId}.png",
                imageAlt: $"{playlist.playlistTitle} picture",
                siteName: $"Playlist - {SITE_NAME}"
            ), "text/html");
        }

        private string GenerateMetaTags(string title, string description, string imageUrl, 
            string imageAlt, string siteName, string themeColor = "#ec018e", string twitterCardType = "summary")
        {
            var sb = new StringBuilder($"""
                    <!DOCTYPE html>
                    <html class="a-fullscreen shoqbzame idc0_350"><head><meta http-equiv="Content-Type" content="text/html">
                    """);
            
            // Basic meta tags
            sb.AppendLine($"<title>{EscapeHtml(title)}</title>");
            sb.AppendLine($"<meta name=\"description\" content=\"{EscapeHtml(description)}\" />");

            sb.AppendLine($"<meta name=\"msapplication-TileColor\" content=\"{themeColor}\" />");
            sb.AppendLine($"<meta name=\"theme-color\" content=\"{themeColor}\" />");
            
            // Open Graph meta tags
            sb.AppendLine($"<meta property=\"og:title\" content=\"{EscapeHtml(title)}\" />");
            sb.AppendLine($"<meta property=\"og:description\" content=\"{EscapeHtml(description)}\" />");
            sb.AppendLine($"<meta property=\"og:image\" content=\"{EscapeHtml(imageUrl)}\" />");
            sb.AppendLine($"<meta property=\"og:site_name\" content=\"{EscapeHtml(siteName)}\" />");
            sb.AppendLine($"<meta property=\"og:type\" content=\"website\" />");
            
            // Twitter Card meta tags
            sb.AppendLine($"<meta name=\"twitter:card\" content=\"{twitterCardType}\" />");
            sb.AppendLine($"<meta name=\"twitter:site\" content=\"{TWITTER_SITE}\" />");
            sb.AppendLine($"<meta name=\"twitter:creator\" content=\"{TWITTER_HANDLE}\" />");
            sb.AppendLine($"<meta name=\"twitter:title\" content=\"{EscapeHtml(title)}\" />");
            sb.AppendLine($"<meta name=\"twitter:description\" content=\"{EscapeHtml(description)}\" />");
            sb.AppendLine($"<meta name=\"twitter:image\" content=\"{EscapeHtml(imageUrl)}\" />");
            sb.AppendLine($"<meta name=\"twitter:image:alt\" content=\"{EscapeHtml(imageAlt)}\" />");

            sb.Append("""
                  </head>
                  <body>
                  </body>
                </html>
                """);
            
            return sb.ToString();
        }

        private string EscapeHtml(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
                
            return input
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        private string GenerateMapsListTitle(string type, string search, double? starsFrom, double? starsTo)
        {
            var title = type switch
            {
                "ranked" => "Ranked Maps",
                "qualified" => "Qualified Maps",
                "nominated" => "Nominated Maps",
                "ost" => "OST Maps",
                _ => "Maps"
            };

            if (!string.IsNullOrEmpty(search))
                title = $"\"{search}\" in {title}";

            if (starsFrom.HasValue && starsTo.HasValue)
                title += $" ({starsFrom.Value:F1}★-{starsTo.Value:F1}★)";
            else if (starsFrom.HasValue)
                title += $" ({starsFrom.Value:F1}★+)";
            else if (starsTo.HasValue)
                title += $" (up to {starsTo.Value:F1}★)";

            return title;
        }

        private string GenerateMapsListDescription(string type, string search, double? starsFrom, double? starsTo)
        {
            var description = type switch
            {
                "ranked" => "List of ranked Beat Saber maps",
                "qualified" => "List of qualified Beat Saber maps",
                "nominated" => "List of nominated Beat Saber maps",
                "ost" => "List of Beat Saber OST maps",
                _ => "Search for Beat Saber maps"
            };

            var filters = new System.Collections.Generic.List<string>();

            if (starsFrom.HasValue && starsTo.HasValue)
                filters.Add($"with star rating between {starsFrom.Value:F1} and {starsTo.Value:F1}");
            else if (starsFrom.HasValue)
                filters.Add($"with star rating above {starsFrom.Value:F1}");
            else if (starsTo.HasValue)
                filters.Add($"with star rating below {starsTo.Value:F1}");

            if (!string.IsNullOrEmpty(search))
                filters.Add($"matching \"{search}\"");

            if (filters.Count > 0)
                description += " " + string.Join(", ", filters);

            return description;
        }

        private string GenerateRankingDescription(ResponseWithMetadata<PlayerResponseWithStats> response)
        {
            var players = response.Data.ToList();
            if (players == null || players.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            var maxLength = 0;
            
            foreach (var player in players.GetRange(0, Math.Min(10, players.Count)))
            {
                if (player.Name.Length > maxLength)
                    maxLength = player.Name.Length;
            }

            foreach (var player in players.GetRange(0, Math.Min(10, players.Count)))
            {
                var rank = $"#{player.Rank:N0}".PadRight(8);
                var name = player.Name.PadRight(maxLength + 2);
                var pp = $"{player.Pp:N0}pp";
                sb.AppendLine($"{rank}{name}{pp}");
            }

            return sb.ToString();
        }

        private string GetRelativeTime(long unixTimestamp)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
            var now = DateTimeOffset.UtcNow;
            var diff = now - date;

            if (diff.TotalDays < 1)
                return $"{(int)diff.TotalHours} hour{((int)diff.TotalHours > 1 ? "s" : "")} ago";
            if (diff.TotalDays < 30)
                return $"{(int)diff.TotalDays} day{((int)diff.TotalDays > 1 ? "s" : "")} ago";
            if (diff.TotalDays < 365)
                return $"{(int)(diff.TotalDays / 30)} month{((int)diff.TotalDays / 30 > 1 ? "s" : "")} ago";
            
            return $"{(int)(diff.TotalDays / 365)} years ago";
        }

        private string GetRelativeTimeFuture(long unixTimestamp)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
            var now = DateTimeOffset.UtcNow;
            var diff = date - now;

            if (diff.TotalDays < 1)
                return $"{(int)diff.TotalHours} hour{((int)diff.TotalHours > 1 ? "s" : "")}";
            if (diff.TotalDays < 30)
                return $"{(int)diff.TotalDays} day{((int)diff.TotalDays > 1 ? "s" : "")}";
            if (diff.TotalDays < 365)
                return $"{(int)(diff.TotalDays / 30)} month{((int)diff.TotalDays / 30 > 1 ? "s" : "")}";
            
            return $"{(int)(diff.TotalDays / 365)} years";
        }

        private string GetCountryName(string countryCode)
        {
            if (string.IsNullOrEmpty(countryCode))
                return "Unknown";
                
            if (countryCode.ToLower() == "not set")
                return "not set";
            
            try
            {
                var regionInfo = new System.Globalization.RegionInfo(countryCode.ToUpper());
                return regionInfo.EnglishName;
            }
            catch (ArgumentException)
            {
                return countryCode.ToUpper();
            }
        }

        private string GetCountryNameFromCodes(string countryCodes)
        {
            if (string.IsNullOrEmpty(countryCodes))
                return "not set";
                
            var codes = countryCodes.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var names = codes.Select(code => GetCountryName(code.Trim())).ToArray();
            
            return string.Join(", ", names);
        }
    }

    public class PlayerData {
        public string Name { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }
        public string CountryName { get; set; }
        public double PP { get; set; }
        public double AverageRankedAccuracy { get; set; }
        public string Avatar { get; set; }
    }

    public class LeaderboardData {
        public string SongName { get; set; }
        public string DifficultyName { get; set; }
        public string ModeName { get; set; }
        public string Author { get; set; }
        public string Mapper { get; set; }
        public string Status { get; set; }
        public double? PassRating { get; set; }
        public double? AccRating { get; set; }
        public double? TechRating { get; set; }
        public string ImageUrl { get; set; }
    }

    public class ScoreData {
        public string PlayerName { get; set; }
        public string SongName { get; set; }
        public string Author { get; set; }
        public int Rank { get; set; }
        public double Accuracy { get; set; }
        public double PP { get; set; }
        public string Mods { get; set; }
        public string DifficultyName { get; set; }
        public long Timepost { get; set; }
        public string SongImageUrl { get; set; }
    }

    public class ClanData {
        public string Name { get; set; }
        public string Tag { get; set; }
        public string Description { get; set; }
        public int PlayersCount { get; set; }
        public string Icon { get; set; }
        public string Color { get; set; }
    }

    public class EventData
    {
        public string Name { get; set; }
        public long EndDate { get; set; }
        public string Image { get; set; }
    }
}


