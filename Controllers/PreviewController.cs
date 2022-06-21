using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Dynamic;
using System.Net;
using System.Web;

namespace BeatLeader_Server.Controllers
{
    public class PreviewController : Controller
    {
        private readonly HttpClient _client;
        private readonly AppContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public PreviewController(AppContext context, IWebHostEnvironment webHostEnvironment) {
            _client = new HttpClient();
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        class SongSelect {
            public string Id { get; set; }
            public string CoverImage { get; set; }
            public string Name { get; set; }
        }

        [HttpGet("~/preview/replay")]
        public async Task<ActionResult> Get(
            [FromQuery] string? playerID = null, 
            [FromQuery] string? id = null, 
            [FromQuery] string? difficulty = null, 
            [FromQuery] string? mode = null,
            [FromQuery] int? scoreId = null) {

            Player? player = null;
            SongSelect? song = null;
            Score? score = null;

            if (playerID != null && id != null) {
                player = _context.Players.Where(p => p.Id == playerID).FirstOrDefault() ?? await GetPlayerFromSS("https://scoresaber.com/api/player/" + playerID + "/full");
                song = _context.Songs.Select(s => new SongSelect { Id = s.Id, CoverImage = s.CoverImage, Name = s.Name }).Where(s => s.Id == id).FirstOrDefault();
            } else if (scoreId != null) {
                score = await _context.Scores.Where(s => s.Id == scoreId).Include(s => s.Player).Include(s => s.Leaderboard).ThenInclude(l => l.Song).FirstOrDefaultAsync();
                if (score != null) {
                    player = score.Player;
                    song = new SongSelect { Id = score.Leaderboard.Song.Id, CoverImage = score.Leaderboard.Song.CoverImage, Name = score.Leaderboard.Song.Name };
                }
            }

            if (player == null || song == null)
            {
                return NotFound();
            }

            int width = 500; int height = 300;
            Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            Graphics graphics = Graphics.FromImage(bitmap);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.DrawImage(new Bitmap(_webHostEnvironment.WebRootPath + "/images/background.png"), new Rectangle(0, 0, width, height));
            graphics.DrawImage(new Bitmap(_webHostEnvironment.WebRootPath + "/images/replays.png"), new Rectangle(width / 2 - 50, height / 2 - 50 - 25, 100, 100));

            using (var request = new HttpRequestMessage(HttpMethod.Get, player.Avatar))
            {
                Stream contentStream = await (await _client.SendAsync(request)).Content.ReadAsStreamAsync();
                Bitmap avatarBitmap = new Bitmap(contentStream);

                graphics.DrawImage(avatarBitmap, new Rectangle(50, 50, 135, 135));
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, song.CoverImage))
            {
                Stream contentStream = await (await _client.SendAsync(request)).Content.ReadAsStreamAsync();
                Bitmap coverBitmap = new Bitmap(contentStream);

                graphics.DrawImage(coverBitmap, new Rectangle(width - 185, 50, 135, 135));
            }

            PrivateFontCollection fontCollection = new PrivateFontCollection();
            fontCollection.AddFontFile(_webHostEnvironment.WebRootPath + "/fonts/Audiowide-Regular.ttf");
            StringFormat stringFormat = new StringFormat();
            stringFormat.Alignment = StringAlignment.Center;

            Color nameColor = ColorFromHSV((Math.Max(0, player.Pp - 1000) / 18000) * 360, 1.0, 1.0);
            graphics.DrawString(player.Name, new Font("Audiowide", 24), new SolidBrush(nameColor), new RectangleF(0, 190, width, 30), stringFormat);

            var songNameFont = new Font("Audiowide", 30 - song.Name.Length / 5);
            graphics.DrawString(song.Name, songNameFont, new SolidBrush(Color.White), 30, new RectangleF(0, 225, width, 80), stringFormat);

            if (score != null) {
                string accuracy = Math.Round(score.Accuracy * 100, 2) + "%";
                SizeF size3 = graphics.MeasureString(accuracy, new Font("Audiowide", 17), width);
                graphics.DrawString(accuracy, new Font("Audiowide", 17), new SolidBrush(Color.White), new Point((int)(120 - size3.Width / 2), 15));

                string rankandpp = "#" + score.Rank + (score.Pp > 0 ? " (" + Math.Round(score.Pp, 2) + "pp)" : "");
                SizeF size4 = graphics.MeasureString(rankandpp, new Font("Audiowide", 17), width);
                graphics.DrawString(rankandpp, new Font("Audiowide", 17), new SolidBrush(Color.White), new Point((int)(width - 120 - size4.Width / 2), 15));
            }
            

            graphics.DrawRectangle(new Pen(new LinearGradientBrush(new Point(1, 1), new Point(100, 100), Color.Red, Color.BlueViolet), 5), new Rectangle(0, 0, width, height));

            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return File(ms.ToArray(), "image/png");
            }
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
                return Color.FromArgb(255, v, t, p);
            else if (hi == 1)
                return Color.FromArgb(255, q, v, p);
            else if (hi == 2)
                return Color.FromArgb(255, p, v, t);
            else if (hi == 3)
                return Color.FromArgb(255, p, q, v);
            else if (hi == 4)
                return Color.FromArgb(255, t, p, v);
            else
                return Color.FromArgb(255, v, p, q);
        }


    }

    public static class GraphicsExtension
    {
        public static IEnumerable<string> GetWrappedLines(this Graphics that, string text, Font font, double maxWidth = double.PositiveInfinity)
        {
            if (String.IsNullOrEmpty(text)) return new string[0];
            if (font == null) throw new ArgumentNullException("font", "The 'font' parameter must not be null");
            if (maxWidth <= 0) throw new ArgumentOutOfRangeException("maxWidth", "Maximum width must be greater than zero");

            // See https://stackoverflow.com/questions/6111298/best-way-to-specify-whitespace-in-a-string-split-operation
            string[] words = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0) return new string[0];

            List<string> lines = new List<string>();

            float spaceCharWidth = that.MeasureString(" ", font).Width;
            float currentWidth = 0;
            string currentLine = "";
            for (int i = 0; i < words.Length; i++)
            {
                float currentWordWidth = that.MeasureString(words[i], font).Width;
                if (currentWidth != 0)
                {
                    float potentialWordWidth = spaceCharWidth + currentWordWidth;
                    if (currentWidth + potentialWordWidth < maxWidth)
                    {
                        currentWidth += potentialWordWidth;
                        currentLine += " " + words[i];
                    }
                    else
                    {
                        lines.Add(currentLine);
                        currentLine = words[i];
                        currentWidth = currentWordWidth;
                    }
                }
                else
                {
                    currentWidth += currentWordWidth;
                    currentLine = words[i];
                }

                if (i == words.Length - 1)
                {
                    lines.Add(currentLine);
                }
            }

            return lines;
        }

        public static void DrawString(this Graphics that, string text, Font font, Brush brush,
                                            int lineHeight, RectangleF layoutRectangle, StringFormat format)
        {
            string[] lines = that.GetWrappedLines(text, font, layoutRectangle.Width).ToArray();
            Rectangle lastDrawn = new Rectangle(Convert.ToInt32(layoutRectangle.X), Convert.ToInt32(layoutRectangle.Y), 0, 0);
            foreach (string line in lines)
            {
                SizeF lineSize = that.MeasureString(line, font);
                int increment = lastDrawn.Height == 0 ? 0 : lineHeight;
                RectangleF lineOrigin = new RectangleF(lastDrawn.X, lastDrawn.Y + increment, layoutRectangle.Width, layoutRectangle.Height);
                that.DrawString(line, font, brush, lineOrigin, format);
                lastDrawn = new Rectangle(Point.Round(lineOrigin.Location), Size.Round(lineSize));
            }
        }
    }
}
