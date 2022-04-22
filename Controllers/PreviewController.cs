using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
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
        private readonly SongController _songController;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public PreviewController(SongController songController, IWebHostEnvironment webHostEnvironment) {
            _client = new HttpClient();
            _songController = songController;
            _webHostEnvironment = webHostEnvironment;
        }
        [HttpGet("~/preview/replay")]
        public async Task<ActionResult> Get([FromQuery] string link)
        {
            Uri myUri = new Uri(link);
            var parameters = HttpUtility.ParseQueryString(myUri.Query);
            string? playerID = parameters.Get("playerID");
            if (playerID == null) {
                return BadRequest("Link should contain playerID");
            }

            string? songID = parameters.Get("id");
            if (songID == null) {
                return BadRequest("Link should contain song id");
            }

            Player? player = await GetPlayerFromSS("https://scoresaber.com/api/player/" + playerID + "/full");
            if (player == null) {
                return NotFound();
            }

            Song? song = null; //await _songController(songID);
            if (song == null) {
                return NotFound();
            }

            int width = 500; int height = 300;
            Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            Graphics graphics = Graphics.FromImage(bitmap);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
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

            SizeF size1 = graphics.MeasureString(player.Name, new Font("Audiowide", 24), width);
            Color nameColor = ColorFromHSV((Math.Max(0, player.Pp - 1000) / 18000) * 360, 1.0, 1.0);
            graphics.DrawString(player.Name, new Font("Audiowide", 24), new SolidBrush(nameColor), new Point((int)(width / 2f - size1.Width / 2), 190));

            SizeF size2 = graphics.MeasureString(song.Name, new Font("Audiowide", 28), width);
            graphics.DrawString(song.Name, new Font("Audiowide", 28), new SolidBrush(Color.White), new Point((int)(width / 2f - size2.Width / 2), 225));
            

            graphics.DrawRectangle(new Pen(new LinearGradientBrush(new Point(1, 1), new Point(100, 100), Color.Red, Color.BlueViolet), 5), new Rectangle(0, 0, width, height));

            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return File(ms.ToArray(), "image/png");
            }
        }

        public Task<Player?> GetPlayerFromSS(string url)
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
}
