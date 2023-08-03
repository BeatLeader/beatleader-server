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

namespace BeatLeader_Server.Controllers
{
    public class PreviewController : Controller
    {
        private readonly HttpClient _client;
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IAmazonS3 _s3Client;
        private readonly IServerTiming _serverTiming;

        private Image<Rgba32> StarImage;
        private Image<Rgba32> AvatarMask;
        private Image<Rgba32> AvatarShadow;
        private Image<Rgba32> GradientMask; 
        private Image<Rgba32> CoverMask;
        private Image<Rgba32> FinalMask;
        private Image<Rgba32> GradientMaskBlurred;
        private FontFamily FontFamily;
        private FontFamily AudiowideFontFamily;
        private IReadOnlyList<FontFamily> FallbackFamilies;

        private EmbedGenerator _generalEmbedGenerator;
        private EmbedGenerator _twitterEmbedGenerator;

        public PreviewController(
            AppContext context,
            ReadAppContext readContext,
            IWebHostEnvironment webHostEnvironment, 
            IServerTiming serverTiming,
            IConfiguration configuration) {
            _client = new HttpClient();
            _context = context;
            _readContext = readContext;
            _webHostEnvironment = webHostEnvironment;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();

            StarImage = LoadImage("Star.png");
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
            fallbackCollection.Add(_webHostEnvironment.WebRootPath + "/fonts/Cyberbit.ttf");
            fallbackCollection.Add(_webHostEnvironment.WebRootPath + "/fonts/seguiemj.ttf");

            FallbackFamilies = fallbackCollection.Families.ToList();

            _generalEmbedGenerator = new EmbedGenerator(
                new Size(500, 300),
                StarImage,
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
                AvatarMask,
                AvatarShadow,
                GradientMask,
                GradientMaskBlurred,
                CoverMask,
                FinalMask,
                FontFamily,
                FallbackFamilies
            );
        }
        class SongSelect {
            public string Id { get; set; }
            public string CoverImage { get; set; }
            public string Name { get; set; }
            public string Hash { get; set; }
        }

        class ScoreSelect {
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

            public string PlayerAvatar { get; set; }
            public string PlayerName { get; set; }
            public string PlayerRole { get; set; }
            public PatreonFeatures? PatreonFeatures { get; set; }
            public ProfileSettings? ProfileSettings { get; set; }
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
        private async Task<ActionResult> GetFromScore(ScoreSelect? score, bool twitter) {
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

            Image<Rgba32>? result = null;
            
            using (_serverTiming.TimeAction("generate"))
            {
                result = await Task.Run(() => (twitter ? _twitterEmbedGenerator : _generalEmbedGenerator).Generate(
                    score.PlayerName,
                    score.SongName,
                    (score.FullCombo ? "FC" : "") + (score.Modifiers.Length > 0 ? (score.FullCombo ? ", " : "") + String.Join(", ", score.Modifiers.Split(",")) : ""),
                    diff,
                    score.Accuracy,
                    score.Rank,
                    score.Pp,
                    score.Stars ?? 0,
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
                    await _s3Client.UploadPreview($"{score.ScoreId}-{(twitter ? "twitter" : "")}preview.png", ms);
                }
                catch { }
            }
            ms.Position = 0;

            return File(ms, "image/png");
        }

        [HttpGet("~/preview/replay")]
        public async Task<ActionResult> Get(
            [FromQuery] string? playerID = null, 
            [FromQuery] string? id = null, 
            [FromQuery] string? difficulty = null, 
            [FromQuery] string? mode = null,
            [FromQuery] string? link = null,
            [FromQuery] int? scoreId = null,
            [FromQuery] bool twitter = false) {

            if (scoreId != null)
            {
                var stream = await _s3Client.DownloadPreview($"{scoreId}-{(twitter ? "twitter" : "")}preview.png");
                if (stream != null)
                {
                    return File(stream, "image/png");
                }
            }
            else if (link != null) {
                return await GetLink(link, twitter);
            }
            else
            {
                return await GetOld(playerID, id, difficulty, mode);
            }

            ScoreSelect? score = null;

            using (_serverTiming.TimeAction("db"))
            {
                if (scoreId != null) {
                    score = await _readContext.Scores
                        .Where(s => s.Id == scoreId)
                        .Include(s => s.Player)
                            .ThenInclude(p => p.ProfileSettings)
                        .Include(s => s.Leaderboard)
                            .ThenInclude(l => l.Song)
                        .Include(s => s.Leaderboard)
                            .ThenInclude(l => l.Difficulty)
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

                            PlayerAvatar = s.Player.Avatar,
                            PlayerName = s.Player.Name,
                            PlayerRole = s.Player.Role,
                            PatreonFeatures = s.Player.PatreonFeatures,
                            ProfileSettings = s.Player.ProfileSettings,
                        })
                        .FirstOrDefaultAsync();
                    if (score == null) {
                        var redirect = _readContext.ScoreRedirects.FirstOrDefault(sr => sr.OldScoreId == scoreId);
                        if (redirect != null && redirect.NewScoreId != scoreId)
                        {
                            return await Get(scoreId: redirect.NewScoreId);
                        }
                    } else {
                        score.ScoreId = scoreId;
                    }
                }
            }

            return await GetFromScore(score, twitter);
        }

        [NonAction]
        public async Task<ActionResult> GetLink(string link, bool twitter) {
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
                .Where(lb => lb.Song.Hash == info.hash && lb.Difficulty.DifficultyName == info.difficulty && lb.Difficulty.ModeName == info.mode)
                .Include(s => s.Song)
                .Include(s => s.Difficulty)
                .Select(s => new ScoreSelect {
                    SongId = s.Song.Id,
                    CoverImage = s.Song.CoverImage,
                    SongName = s.Song.Name,
                    Stars = s.Difficulty.Stars,
                    Difficulty = s.Difficulty.DifficultyName,
                    Accuracy = s.Difficulty.MaxScore
                })
                .FirstOrDefaultAsync();

            var playerId = info.playerID;
            long intId = Int64.Parse(playerId);
            if (intId < 70000000000000000)
            {
                AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == intId);
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

            return await GetFromScore(score, twitter);
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
                player = _context.Players.FirstOrDefault(p => p.Id == playerID) ?? await GetPlayerFromSS("https://scoresaber.com/api/player/" + playerID + "/full");
                song = _context.Songs.Select(s => new SongSelect { Id = s.Id, CoverImage = s.CoverImage, Name = s.Name }).FirstOrDefault(s => s.Id == id);
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
            var textOptions = new TextOptions(font);
            textOptions.TextAlignment = TextAlignment.Center;
            textOptions.HorizontalAlignment = HorizontalAlignment.Center;
            textOptions.Dpi = 96;
            textOptions.Origin = new PointF(width / 2, 185);
            textOptions.WrappingLength = width;
            textOptions.FallbackFontFamilies = FallbackFamilies;

            image.Mutate(x => x.DrawText(textOptions, player.Name, color));

            var songNameFont = new Font(AudiowideFontFamily, 26 -song.Name.Length / 5);
            textOptions = new TextOptions(songNameFont);
            textOptions.TextAlignment = TextAlignment.Center;
            textOptions.HorizontalAlignment = HorizontalAlignment.Center;
            textOptions.Dpi = 96;
            textOptions.Origin = new PointF(width / 2, 224);
            textOptions.WrappingLength = width;
            textOptions.LineSpacing = 0.8f;
            textOptions.FallbackFontFamilies = FallbackFamilies;

            image.Mutate(x => x.DrawText(textOptions, song.Name, Color.White));
            image.Mutate(x => x.Draw(new Pen(new LinearGradientBrush(new Point(1, 1), new Point(100, 100), GradientRepetitionMode.Repeat, new ColorStop(0, Color.Red), new ColorStop(1, Color.BlueViolet)), 5), new Rectangle(0, 0, width, height)));

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
                playersList = _context.Players.Where(p => ids.Contains(p.Id)).ToList();
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

                song = _context.Songs.Select(s => new SongSelect { Hash = s.Hash, CoverImage = s.CoverImage, Name = s.Name }).FirstOrDefault(s => s.Hash == hash);
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

            var textOptions = new TextOptions(new Font(AudiowideFontFamily, 9));
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

                var nameOptions = new TextOptions(new Font(AudiowideFontFamily, 14 - (player.Name.Length / 5) - (player.Name.Length > 15 ? 3 : 0)));
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

            var left = new TextOptions(new Font(AudiowideFontFamily, 12));
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
            textOptions = new TextOptions(songNameFont);
            textOptions.TextAlignment = TextAlignment.Center;
            textOptions.HorizontalAlignment = HorizontalAlignment.Center;
            textOptions.Dpi = 96;
            textOptions.Origin = new PointF(width / 2, 224);
            textOptions.WrappingLength = width;
            textOptions.LineSpacing = 0.8f;
            textOptions.FallbackFontFamilies = FallbackFamilies;

            image.Mutate(x => x.DrawText(textOptions, song.Name, Color.White));
            image.Mutate(x => x.Draw(new Pen(new LinearGradientBrush(new Point(1, 1), new Point(100, 100), GradientRepetitionMode.Repeat, new ColorStop(0, Color.Red), new ColorStop(1, Color.BlueViolet)), 5), new Rectangle(0, 0, width, height)));

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
