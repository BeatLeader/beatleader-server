using Amazon.S3;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using System.Dynamic;
using System.Net;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using ReplayDecoder;
using System.Linq.Expressions;

namespace BeatLeader_Server.Controllers
{
    public class PreviewController : Controller
    {
        private readonly HttpClient _client;
        private readonly AppContext _context;

        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IAmazonS3 _s3Client;
        private readonly IServerTiming _serverTiming;

        private static Image<Rgba32> StarImage;
        private static Image<Rgba32> AvatarMask;
        private static Image<Rgba32> NinetyDegreeImage;
        private static Image<Rgba32> ThreeSixtyDegreeImage;
        private static Image<Rgba32> LawlessImage;
        private static Image<Rgba32> LegacyImage;
        private static Image<Rgba32> LightshowImage;
        private static Image<Rgba32> NoArrowsImage;
        private static Image<Rgba32> OneSaberImage;
        private static Image<Rgba32> StandardImage;
        private static Image<Rgba32> AvatarShadow;
        private static Image<Rgba32> GradientMask; 
        private static Image<Rgba32> CoverMask;
        private static Image<Rgba32> FinalMask;
        private static Image<Rgba32> GradientMaskBlurred;
        private static FontFamily FontFamily;
        private static FontFamily AudiowideFontFamily;
        private static IReadOnlyList<FontFamily> FallbackFamilies;

        private static EmbedGenerator _generalEmbedGenerator;
        private static EmbedGenerator _twitterEmbedGenerator;

        public PreviewController(
            AppContext context,
            IWebHostEnvironment webHostEnvironment, 
            IServerTiming serverTiming,
            IConfiguration configuration) {
            _client = new HttpClient();
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();

            if (StarImage == null) {
                StarImage = LoadImage("Star.png");
                NinetyDegreeImage = LoadImage("90degree.png");
                ThreeSixtyDegreeImage = LoadImage("360degree.png");
                LawlessImage = LoadImage("lawless.png");
                LegacyImage = LoadImage("legacy.png");
                LightshowImage = LoadImage("lightshow.png");
                NoArrowsImage = LoadImage("noarrows.png");
                OneSaberImage = LoadImage("onesaber.png");
                StandardImage = LoadImage("standard.png");

                AvatarMask = LoadImage("AvatarMask.png");
                AvatarShadow = LoadImage("AvatarShadow.png");
                GradientMask = LoadImage("GradientMask.png");
                CoverMask = LoadImage("CoverMask.png");
                FinalMask = LoadImage("FinalMask.png");
                GradientMaskBlurred = LoadImage("GradientMaskBlurred.png");

                var fontCollection = new FontCollection();
                fontCollection.Add(_webHostEnvironment.WebRootPath + "/fonts/Teko-SemiBold.ttf");
                FontFamily = fontCollection.Families.First();

                var fontCollection2 = new FontCollection();
                fontCollection2.Add(_webHostEnvironment.WebRootPath + "/fonts/Audiowide-Regular.ttf");
                AudiowideFontFamily = fontCollection2.Families.First();

                var fallbackCollection = new FontCollection();

                var fonts = Directory.GetFiles(_webHostEnvironment.WebRootPath + "/fonts/", "*.ttf", SearchOption.AllDirectories);
                foreach (var f in fonts)
                {
                    fallbackCollection.Add(f);
                }

                FallbackFamilies = fallbackCollection.Families.ToList();
            }
        }
        class SongSelect {
            public string Id { get; set; }
            public string CoverImage { get; set; }
            public string Name { get; set; }
            public string Hash { get; set; }
        }

        public class ScoreSelect {
            public string SongId { get; set; }
            public string CoverImage { get; set; }
            public string SongName { get; set; }
            public float? Stars { get; set; }
            public int? ScoreId { get; set; }

            public float Accuracy { get; set; }
            public string PlayerId { get; set; }
            public float Pp { get; set; }
            public int Rank { get; set; }
            public string Modifiers { get; set; }
            public bool FullCombo { get; set; }
            public string Difficulty { get; set; }
            public string ModeName { get; set; }
            public string PlayerAvatar { get; set; }
            public string PlayerName { get; set; }
            public string PlayerRole { get; set; }
            public PatreonFeatures? PatreonFeatures { get; set; }
            public ProfileSettings? ProfileSettings { get; set; }
            public ICollection<ScoreContextExtension> ContextExtensions { get; set; }

            public LeaderboardContexts Context { get; set; } = LeaderboardContexts.General;

            public void ToContext(ScoreContextExtension? extension)
            {
                if (extension == null) return;

                Rank = extension.Rank;
                Accuracy = extension.Accuracy;
                Pp = extension.Pp;
                Modifiers = extension.Modifiers;
            }
        }

        private Image<Rgba32> LoadImage(string fileName)
        {
            return Image.Load<Rgba32>(_webHostEnvironment.WebRootPath + "/images/" + fileName);
        }

        private async Task<Image<Rgba32>> LoadRemoteImage(string url)
        {
            Image<Rgba32>? result = null;
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                try {
                    Stream contentStream = await (await _client.SendAsync(request)).Content.ReadAsStreamAsync();
                    result = Image.Load<Rgba32>(contentStream);
                } catch { }
            }
            return result ?? LoadImage("default.jpg");
        }

        private async Task<Image<Rgba32>?> LoadOverlayImage(ScoreSelect score)
        {
            ResponseUtils.PostProcessSettings(score.PlayerRole, score.ProfileSettings, score.PatreonFeatures);
            if (score.ProfileSettings?.EffectName == null || score.ProfileSettings.EffectName.Length == 0) return null;

            using (var stream = await _s3Client.DownloadAsset(score.ProfileSettings.EffectName + "_Effect.png"))
            {
                if (stream != null) {
                    return Image.Load<Rgba32>(stream);
                } else {
                    return null;
                }
            } 
        }

        [NonAction]
        public async Task<ActionResult> GetFromScore(ScoreSelect? score) {
            if (score == null)
            {
                return NotFound();
            }

            Image<Rgba32> avatarImage;
            Image<Rgba32> coverImage;
            Image<Rgba32>? overlayImage;

            using (_serverTiming.TimeAction("images"))
            {
               var images = await Task.WhenAll(LoadRemoteImage(score.PlayerAvatar), LoadRemoteImage(score.CoverImage), LoadOverlayImage(score));
               (avatarImage, coverImage, overlayImage) = (images[0], images[1], images[2]);
            }

            (Color diffColor, string diff) = DiffColorAndName(score.Difficulty);

            if (score.ContextExtensions?.Count > 0) {
                var bestScore = score.ContextExtensions.Where(s => s.Context != LeaderboardContexts.Speedrun && s.Context != LeaderboardContexts.Funny).OrderBy(ce => ce.Rank).FirstOrDefault();
                if (bestScore != null && (bestScore.Rank < score.Rank || score.Rank == 0) && bestScore.Rank != 0) {
                    score.ToContext(bestScore);
                    score.Context = bestScore.Context;
                }
            }

            Image<Rgba32>? result = null;

            Image<Rgba32>? ModeImage = score.ModeName.Trim().ToUpper() switch
            {
                "90DEGREE" => NinetyDegreeImage,
                "360DEGREE" => ThreeSixtyDegreeImage,
                "LAWLESS" => LawlessImage,
                "LEGACY" => LegacyImage,
                "LIGHTSHOW" => LightshowImage,
                "NOARROWS" => NoArrowsImage,
                "ONESABER" => OneSaberImage,
                _ => StandardImage
            };

            _generalEmbedGenerator = new EmbedGenerator(
                new Size(500, 300),
                StarImage,
                ModeImage,
                AvatarMask,
                AvatarShadow,
                GradientMask,
                GradientMaskBlurred,
                CoverMask,
                FinalMask,
                FontFamily,
                FallbackFamilies
            );

            _twitterEmbedGenerator = new EmbedGenerator(
                new Size(512, 268),
                StarImage,
                ModeImage,
                AvatarMask,
                AvatarShadow,
                GradientMask,
                GradientMaskBlurred,
                CoverMask,
                FinalMask,
                FontFamily,
                FallbackFamilies
            );

            using (_serverTiming.TimeAction("generate"))
            {
                result = await Task.Run(() => _twitterEmbedGenerator.Generate(
                    score.PlayerName,
                    score.SongName,
                    (score.FullCombo ? "FC" : "") + (score.Modifiers.Length > 0 ? (score.FullCombo ? ", " : "") + String.Join(", ", score.Modifiers.Split(",")) : ""),
                    diff,
                    score.Accuracy,
                    score.Rank,
                    score.Pp,
                    score.Stars ?? 0,
                    score.Context,
                    coverImage,
                    avatarImage,
                    overlayImage,
                    score.ProfileSettings?.Hue != null ? (int)score.ProfileSettings?.Hue : 0,
                    score.ProfileSettings?.Saturation ?? 1,
                    score.ProfileSettings?.LeftSaberColor?.Length > 0 ? Color.Parse(score.ProfileSettings.LeftSaberColor) : new Color(new Rgba32(250, 20, 255, 255)),
                    score.ProfileSettings?.RightSaberColor?.Length > 0 ? Color.Parse(score.ProfileSettings.RightSaberColor) : new Color(new Rgba32(128, 0, 255, 255)),
                    diffColor
                ));
            }
            
            MemoryStream ms = new MemoryStream();
            result.SaveAsPng(ms);
            ms.Position = 0;
            if (score.ScoreId != null)
            {
                try
                {
                    await _s3Client.UploadPreview($"{score.ScoreId}preview.png", ms);
                }
                catch { }
            }
            ms.Position = 0;

            return File(ms, "image/png");
        }

        [HttpGet("~/preview/replay")]
        [HttpGet("~/preview/replay.png")]
        public async Task<ActionResult> Get(
            [FromQuery] string? playerID = null, 
            [FromQuery] string? id = null, 
            [FromQuery] string? difficulty = null, 
            [FromQuery] string? mode = null,
            [FromQuery] string? link = null,
            [FromQuery] int? scoreId = null) {

            if (scoreId != null)
            {
                var previewUrl = await _s3Client.GetPresignedUrl($"{scoreId}preview.png", S3Container.previews);
                if (previewUrl != null)
                {
                    return Redirect(previewUrl);
                }
            }
            else if (link != null) {
                return await GetLink(link);
            }
            else
            {
                return await GetOld(playerID, id, difficulty, mode);
            }

            ScoreSelect? score = null;

            using (_serverTiming.TimeAction("db"))
            {
                if (scoreId != null) {
                    score = await _context.Scores
                        .Where(s => s.Id == scoreId)
                        .Include(s => s.Player)
                            .ThenInclude(p => p.ProfileSettings)
                        .Include(s => s.Leaderboard)
                            .ThenInclude(l => l.Song)
                        .Include(s => s.Leaderboard)
                            .ThenInclude(l => l.Difficulty)
                        .Include(s => s.ContextExtensions)
                        .Select(s => new ScoreSelect {
                            SongId = s.Leaderboard.Song.Id,
                            CoverImage = s.Leaderboard.Song.CoverImage,
                            SongName = s.Leaderboard.Song.Name,
                            Stars = 
                                (s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                                s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                                s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) ? s.Leaderboard.Difficulty.Stars : null,

                            Accuracy = s.Accuracy,
                            PlayerId = s.PlayerId,
                            Pp = s.Pp,
                            Rank = s.Rank,
                            Modifiers = s.Modifiers,
                            FullCombo = s.FullCombo,
                            Difficulty = s.Leaderboard.Difficulty.DifficultyName,
                            ModeName = s.Leaderboard.Difficulty.ModeName,

                            PlayerAvatar = s.Player.Avatar,
                            PlayerName = s.Player.Name,
                            PlayerRole = s.Player.Role,
                            PatreonFeatures = s.Player.PatreonFeatures,
                            ProfileSettings = s.Player.ProfileSettings,
                            ContextExtensions = s.ContextExtensions
                        })
                        .FirstOrDefaultAsync();
                    if (score == null) {
                        var redirect = await _context.ScoreRedirects.FirstOrDefaultAsync(sr => sr.OldScoreId == scoreId);
                        if (redirect != null && redirect.NewScoreId != scoreId)
                        {
                            return await Get(scoreId: redirect.NewScoreId);
                        }
                    } else {
                        score.ScoreId = scoreId;
                    }
                }
            }

            return await GetFromScore(score);
        }

        [HttpGet("~/preview/prerender/replay")]
        public async Task<ContentResult> GetPrerender(
            [FromQuery] string? playerID = null, 
            [FromQuery] string? id = null, 
            [FromQuery] string? difficulty = null, 
            [FromQuery] string? mode = null,
            [FromQuery] string? link = null,
            [FromQuery] int? scoreId = null) {

            ScoreSelect? score = null;

            using (_serverTiming.TimeAction("db"))
            {
                var query = scoreId != null 
                    ? _context.Scores
                        .Where(s => s.Id == scoreId)
                    : _context.Scores
                        .Where(s => s.PlayerId == playerID && s.Leaderboard.Difficulty.DifficultyName == difficulty && s.Leaderboard.Difficulty.ModeName == mode);
                
                score = await query
                    .Select(s => new ScoreSelect {
                        ScoreId = s.Id,
                        SongName = s.Leaderboard.Song.Name,
                        PlayerName = s.Player.Name
                    })
                    .FirstOrDefaultAsync();
                if (score == null) {
                    var redirect = await _context.ScoreRedirects.FirstOrDefaultAsync(sr => sr.OldScoreId == scoreId);
                    if (redirect != null && redirect.NewScoreId != scoreId)
                    {
                        return await GetPrerender(scoreId: redirect.NewScoreId);
                    }
                } else if (scoreId != null){
                    score.ScoreId = scoreId;
                }
            }

            if (score != null) {
                Get(scoreId: score.ScoreId);
            }

            var title = score != null ? $"Replay | {score.PlayerName} | {score.SongName}" : "Beat Saber Web Replays viewer";

            return new ContentResult 
            {
                ContentType = "text/html",
                Content = $"""
                    <!DOCTYPE html>
                    <html class="a-fullscreen shoqbzame idc0_350"><head><meta http-equiv="Content-Type" content="text/html">
                        <title>{title}</title>
                        
                        <link rel="apple-touch-icon-precomposed" sizes="57x57" href="https://replay.beatleader.com/assets/img/apple-touch-icon-57x57.png">
                        <link rel="apple-touch-icon-precomposed" sizes="114x114" href="https://replay.beatleader.com/assets/img/apple-touch-icon-114x114.png">
                        <link rel="apple-touch-icon-precomposed" sizes="72x72" href="https://replay.beatleader.com/assets/img/apple-touch-icon-72x72.png">
                        <link rel="apple-touch-icon-precomposed" sizes="144x144" href="https://replay.beatleader.com/assets/img/apple-touch-icon-144x144.png">
                        <link rel="apple-touch-icon-precomposed" sizes="60x60" href="https://replay.beatleader.com/assets/img/apple-touch-icon-60x60.png">
                        <link rel="apple-touch-icon-precomposed" sizes="120x120" href="https://replay.beatleader.com/assets/img/apple-touch-icon-120x120.png">
                        <link rel="apple-touch-icon-precomposed" sizes="76x76" href="https://replay.beatleader.com/assets/img/apple-touch-icon-76x76.png">
                        <link rel="apple-touch-icon-precomposed" sizes="152x152" href="https://replay.beatleader.com/assets/img/apple-touch-icon-152x152.png">
                        <link rel="icon" type="image/png" href="https://replay.beatleader.com/assets/img/favicon-96x96.png" sizes="96x96">
                        <link rel="icon" type="image/png" href="https://replay.beatleader.com/assets/img/favicon-32x32.png" sizes="32x32">
                        <link rel="icon" type="image/png" href="https://replay.beatleader.com/assets/img/favicon-16x16.png" sizes="16x16">
                        <link rel="icon" type="image/png" href="https://replay.beatleader.com/assets/img/favicon-128.png" sizes="128x128">
                        <meta name="application-name" content="Beat Saber web replays">
                        <meta name="msapplication-TileColor" content="#FFFFFF">
                        <meta name="msapplication-TileImage" content="assets/img/mstile-144x144.png">
                        <meta name="msapplication-square70x70logo" content="assets/img/mstile-70x70.png">
                        <meta name="msapplication-square150x150logo" content="assets/img/mstile-150x150.png">
                        <meta name="msapplication-wide310x150logo" content="assets/img/mstile-310x150.png">
                        <meta name="msapplication-square310x310logo" content="assets/img/mstile-310x310.png">

                        <meta property="og:title" content="{title}">
                        <meta property="og:description" content="Beat Saber web replays">
                        <meta property="twitter:description" content="Beat Saber web replays">
                        <meta name="twitter:title" content="{title}">
                        <meta name="twitter:site" content="replay.beatleader.com">
                        <meta name="twitter:image:alt" content="Beat Saber replay">

                        <meta name="twitter:player:width" content="700">
                        <meta name="twitter:player:height" content="400">
                        <meta name="twitter:card" content="{(string.Join("", (string[])Request.Headers.UserAgent).ToLower().Contains("bsky") ? "player" : "summary_large_image")}">

                        <meta property="og:type" content="website">
                        <meta property="og:image:width" content="700">
                        <meta property="og:image:height" content="400">

                        {(score != null ? $"""
                        <meta property="og:url" content="https://replay.beatleader.com/?scoreId={score.ScoreId}">
                        <meta property="twitter:player" content="https://replay.beatleader.com/?scoreId={score.ScoreId}">
                        <meta property="twitter:image" content="https://api.beatleader.com/preview/replay.png?scoreId={score.ScoreId}">
                        <meta property="og:image" content="https://api.beatleader.com/preview/replay.png?scoreId={score.ScoreId}">
                        """ : """
                        <meta property="og:url" content="https://replay.beatleader.com">
                        <meta property="twitter:player" content="https://replay.beatleader.com">
                        <meta property="twitter:image" content="https://replay.beatleader.com/preview.png">
                        <meta property="og:image" content="https://replay.beatleader.com/preview.png">
                        """)}
                      </head>
                      <body>
                      </body>
                    </html>
                    """
            };
        }

        [NonAction]
        public async Task<ActionResult> GetLink(string link) {
            ReplayInfo? info = null;

            if (!link.EndsWith("bsor")) {
                return BadRequest("Not a BSOR");
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, link))
            {
                try {
                    Stream contentStream = await (await _client.SendAsync(request)).Content.ReadAsStreamAsync();

                    (info, _) = await new AsyncReplayDecoder().StartDecodingStream(contentStream);
                } catch { }
            }

            if (info == null) {
                return NotFound();
            }

            var score = await _context.Leaderboards
                .Where(lb => lb.Song.LowerHash == info.hash && lb.Difficulty.DifficultyName == info.difficulty && lb.Difficulty.ModeName == info.mode)
                .Select(s => new ScoreSelect {
                    SongId = s.Song.Id,
                    CoverImage = s.Song.CoverImage,
                    SongName = s.Song.Name,
                    Stars = s.Difficulty.Stars,
                    Difficulty = s.Difficulty.DifficultyName,
                    Accuracy = s.Difficulty.MaxScore
                })
                .FirstOrDefaultAsync();

            if (score == null) {
                return NotFound();
            }

            var playerId = info.playerID;
            long intId = Int64.Parse(playerId);
            if (intId < 70000000000000000)
            {
                AccountLink? accountLink = await _context.AccountLinks.FirstOrDefaultAsync(el => el.OculusID == intId);
                if (accountLink != null) {
                    playerId = accountLink.SteamID.Length > 0 ? accountLink.SteamID : accountLink.PCOculusID;
                }
            }

            var player = await _context.Players.Include(p => p.ProfileSettings).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player != null) {
                score.PlayerAvatar = player.Avatar;
                score.PlayerName = player.Name;
                score.PlayerRole = player.Role;
                score.PatreonFeatures = player.PatreonFeatures;
                score.ProfileSettings = player.ProfileSettings;
                score.PlayerId = playerId;
            }

            score.Accuracy = (float)info.score / score.Accuracy;
            score.Modifiers = info.modifiers;

            return await GetFromScore(score);
        }

        [NonAction]
        public async Task<ActionResult> GetOld(
            [FromQuery] string? playerID = null,
            [FromQuery] string? id = null,
            [FromQuery] string? difficulty = null,
            [FromQuery] string? mode = null)
        {
            Player? player = null;
            SongSelect? song = null;

            if (playerID != null && id != null)
            {
                player = await _context.Players.FirstOrDefaultAsync(p => p.Id == playerID) ?? await GetPlayerFromSS("https://scoresaber.com/api/player/" + playerID + "/full");
                song = await _context.Songs.Select(s => new SongSelect { Id = s.Id, CoverImage = s.CoverImage, Name = s.Name }).FirstOrDefaultAsync(s => s.Id == id);
            }

            if (player == null || song == null)
            {
                return NotFound();
            }

            int width = 500; int height = 300;

            Image<Rgba32> image = new Image<Rgba32>(width, height);

            image.Mutate(x => x
                .DrawImage(Image.Load<Rgba32>(_webHostEnvironment.WebRootPath + "/images/backgroundold.png").Resized(new Size(width, height)), new Point(0, 0), 1)
                .DrawImage(Image.Load<Rgba32>(_webHostEnvironment.WebRootPath + "/images/replays.png").Resized(new Size(100, 100)), new Point(width / 2 - 50, height / 2 - 50 - 25), 1)
            );

            using (var request = new HttpRequestMessage(HttpMethod.Get, player.Avatar))
            {
                Stream contentStream = await (await _client.SendAsync(request)).Content.ReadAsStreamAsync();
                Image<Rgba32> avatar = Image.Load<Rgba32>(contentStream);

                image.Mutate(x => x.DrawImage(avatar.Resized(new Size(135, 135)), new Point(50, 50), 1));
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get,song.CoverImage))
            {
                Stream contentStream = await (await _client.SendAsync(request)).Content.ReadAsStreamAsync();
                Image<Rgba32> cover = Image.Load<Rgba32>(contentStream);

                image.Mutate(x => x.DrawImage(cover.Resized(new Size(135, 135)), new Point(width - 185, 50), 1));
            }

            var font = new Font(AudiowideFontFamily, 26 - (player.Name.Length / 5) - (player.Name.Length > 15 ? 3 : 0));
            var color = ColorFromHSV((Math.Max(0, player.Pp - 1000) / 18000) * 360, 1.0, 1.0);
            var textOptions = new RichTextOptions(font);
            textOptions.TextAlignment = TextAlignment.Center;
            textOptions.HorizontalAlignment = HorizontalAlignment.Center;
            textOptions.Dpi = 96;
            textOptions.Origin = new PointF(width / 2, 185);
            textOptions.WrappingLength = width;
            textOptions.FallbackFontFamilies = FallbackFamilies;

            image.Mutate(x => x.DrawText(textOptions, player.Name, color));

            var songNameFont = new Font(AudiowideFontFamily, 26 -song.Name.Length / 5);
            textOptions = new RichTextOptions(songNameFont);
            textOptions.TextAlignment = TextAlignment.Center;
            textOptions.HorizontalAlignment = HorizontalAlignment.Center;
            textOptions.Dpi = 96;
            textOptions.Origin = new PointF(width / 2, 224);
            textOptions.WrappingLength = width;
            textOptions.LineSpacing = 0.8f;
            textOptions.FallbackFontFamilies = FallbackFamilies;

            image.Mutate(x => x.DrawText(textOptions, song.Name, Color.White));
            image.Mutate(x => x.Draw(new SolidPen(new LinearGradientBrush(new Point(1, 1), new Point(100, 100), GradientRepetitionMode.Repeat, new ColorStop(0, Color.Red), new ColorStop(1, Color.BlueViolet)), 5), new Rectangle(0, 0, width, height)));

            MemoryStream ms = new MemoryStream();
            WebpEncoder webpEncoder = new()
            {
                NearLossless = true,
                NearLosslessQuality = 80,
                TransparentColorMode = WebpTransparentColorMode.Preserve,
                Quality = 75,
            };

            image.SaveAsWebp(ms, webpEncoder);
            ms.Position = 0;
            return File(ms, "image/webp");
        }

        [HttpGet("~/preview/royale")]
        public async Task<ActionResult> GetRoyale(
            [FromQuery] string? players = null,
            [FromQuery] string? hash = null,
            [FromQuery] string? difficulty = null,
            [FromQuery] string? mode = null)
        {
            List<Player> playersList = new List<Player>();
            SongSelect? song = null;

            if (players != null && hash != null)
            {
                var ids = players.Split(",");
                playersList = await _context.Players.Where(p => ids.Contains(p.Id)).ToListAsync();
                foreach (var id in ids)
                {
                    if (playersList.FirstOrDefault(p => p.Id == id) == null)
                    {
                        Player? ssplayer = await GetPlayerFromSS("https://scoresaber.com/api/player/" + id + "/full");
                        if (ssplayer != null)
                        {
                            playersList.Add(ssplayer);
                        }
                    }
                }

                song = await _context
                    .Songs.Select(s => new SongSelect { Hash = s.LowerHash, CoverImage = s.CoverImage, Name = s.Name })
                    .FirstOrDefaultAsync(s => s.Hash == hash);
            }

            if (playersList.Count == 0 || song == null)
            {
                return NotFound();
            }

            int width = 500; int height = 300;
            Image<Rgba32> image = new Image<Rgba32>(width, height);

            image.Mutate(x => x
                .DrawImage(Image.Load<Rgba32>(_webHostEnvironment.WebRootPath + "/images/backgroundold.png").Resized(new Size(width, height)), new Point(0, 0), 1)
                .DrawImage(Image.Load<Rgba32>(_webHostEnvironment.WebRootPath + "/images/royale.png").Resized(new Size(90, 90)), new Point(width - 130, 20), 1)
            );

            var textOptions = new RichTextOptions(new Font(AudiowideFontFamily, 9));
            textOptions.TextAlignment = TextAlignment.Center;
            textOptions.Dpi = 96;
            textOptions.Origin = new PointF(width - 120, 12);
            textOptions.WrappingLength = 100;
            textOptions.LineSpacing = 0.8f;
            textOptions.FallbackFontFamilies = FallbackFamilies;

            image.Mutate(x => x.DrawText(textOptions, "BS", new Color(new Rgba32(233, 2, 141))));

            textOptions.Origin = new PointF(width - 120, 106);
            image.Mutate(x => x.DrawText(textOptions, "Battle royale!", new Color(new Rgba32(233, 2, 141))));

            for (int i = 0; i < playersList.Count; i++)
            {
                var player = playersList[i];

                using (var request = new HttpRequestMessage(HttpMethod.Get, player.Avatar))
                {
                    Stream contentStream = await (await _client.SendAsync(request)).Content.ReadAsStreamAsync();
                    Image<Rgba32> avatar = Image.Load<Rgba32>(contentStream);

                    image.Mutate(x => x.DrawImage(avatar.Resized(new Size(20, 20)), new Point(18, 15 + i * 27), 1));
                }

                var nameOptions = new RichTextOptions(new Font(AudiowideFontFamily, 14 - (player.Name.Length / 5) - (player.Name.Length > 15 ? 3 : 0)));
                nameOptions.Dpi = 96;
                nameOptions.Origin = new PointF(40, 15 + i * 27);
                nameOptions.WrappingLength = width / 2;
                nameOptions.LineSpacing = 0.8f;
                nameOptions.FallbackFontFamilies = FallbackFamilies;

                image.Mutate(x => x.DrawText(nameOptions, player.Name, Color.White));
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, song.CoverImage))
            {
                Stream contentStream = await (await _client.SendAsync(request)).Content.ReadAsStreamAsync();
                Image<Rgba32> cover = Image.Load<Rgba32>(contentStream);

                image.Mutate(x => x.DrawImage(cover.Resized(new Size(135, 135)), new Point(width / 2 - 40, 15), 1));
            }

            var left = new RichTextOptions(new Font(AudiowideFontFamily, 12));
            left.TextAlignment = TextAlignment.Start;
            left.Dpi = 96;
            left.Origin = new PointF((int)(width / 2 - 44), 160);
            left.FallbackFontFamilies = FallbackFamilies;

            if (difficulty != null)
            {
                (Color color, string diff) = DiffColorAndName(difficulty);
                image.Mutate(x => x.DrawText(left, diff, color));
            }

            var songNameFont = new Font(AudiowideFontFamily, 26 -song.Name.Length / 5);
            textOptions = new RichTextOptions(songNameFont);
            textOptions.TextAlignment = TextAlignment.Center;
            textOptions.HorizontalAlignment = HorizontalAlignment.Center;
            textOptions.Dpi = 96;
            textOptions.Origin = new PointF(width / 2, 224);
            textOptions.WrappingLength = width;
            textOptions.LineSpacing = 0.8f;
            textOptions.FallbackFontFamilies = FallbackFamilies;

            image.Mutate(x => x.DrawText(textOptions, song.Name, Color.White));
            image.Mutate(x => x.Draw(new SolidPen(new LinearGradientBrush(new Point(1, 1), new Point(100, 100), GradientRepetitionMode.Repeat, new ColorStop(0, Color.Red), new ColorStop(1, Color.BlueViolet)), 5), new Rectangle(0, 0, width, height)));

            MemoryStream ms = new MemoryStream();
            WebpEncoder webpEncoder = new()
            {
                NearLossless = true,
                NearLosslessQuality = 80,
                TransparentColorMode = WebpTransparentColorMode.Preserve,
                Quality = 75,
            };

            image.SaveAsWebp(ms, webpEncoder);
            ms.Position = 0;
            return File(ms, "image/webp");
        }

        private Task<Player?> GetPlayerFromSS(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Proxy = null;

            WebResponse? response = null;
            Player? song = null;
            var stream = 
            Task<(WebResponse?, Player?)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    song = null;
                }
            
                return (response, song);
            }, request);

            return stream.ContinueWith(t => ReadPlayerFromResponse(t.Result));
        }

        private Player? ReadPlayerFromResponse((WebResponse?, Player?) response)
        {
            if (response.Item1 != null) {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(results))
                    {
                        return null;
                    }

                    dynamic? info = JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                    if (info == null) return null;

                    Player result = new Player();
                    result.Name = info.name;
                    result.Avatar = info.profilePicture;
                    result.Country = info.country;
                    result.Pp = (int)info.pp;

                    return result;
                }
            } else {
                return response.Item2;
            }
            
        }

        public static Color ColorFromHSV(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            if (hi == 0)
                return new Color(new Rgba32(v, t, p));
            else if (hi == 1)
                return new Color(new Rgba32(q, v, p));
            else if (hi == 2)
                return new Color(new Rgba32(p, v, t));
            else if (hi == 3)
                return new Color(new Rgba32(p, q, v));
            else if (hi == 4)
                return new Color(new Rgba32(t, p, v));
            else
                return new Color(new Rgba32(v, p, q));
        }

        public static (Color, string) DiffColorAndName(string diff) {
            switch (diff)
            {
                case "Easy": return (new Color(new Rgba32(0, 159, 72)), "Easy");
                case "Normal": return (new Color(new Rgba32(28, 156, 255)), "Normal");
                case "Hard": return (new Color(new Rgba32(255, 99, 71)), "Hard");
                case "Expert": return (new Color(new Rgba32(227, 54, 172)), "Expert");
                case "ExpertPlus": return (new Color(new Rgba32(143, 72, 219)), "Expert+");
            }
            return (Color.White, diff);
        }
    }
}
