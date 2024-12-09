
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PuppeteerSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Web;

namespace Renderer.Controllers
{
    public class ScreenshotController : Controller
    {
        private static BrowserPool? browserPool;
        private readonly IConfiguration configuration;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IMemoryCache _memoryCache;

        public ScreenshotController(IConfiguration configuration, IWebHostEnvironment webHostEnvironment, IMemoryCache memoryCache)
        {
            this.configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
            _memoryCache = memoryCache;
            if (browserPool == null) {
                browserPool = new BrowserPool(20);
            }
        }

        [HttpGet("/prerender")]
        public async Task<IActionResult> Prerender([FromQuery] string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return BadRequest("URL is required");
            }

            string cacheKey = $"prerender-{url}";
            if (_memoryCache.TryGetValue(cacheKey, out string? cachedHtml))
            {
                return Content(cachedHtml ?? "", "text/html");
            }

            var browser = await browserPool?.GetBrowserAsync();

            using (var page = await browser.NewPageAsync())
            {
                await page.SetUserAgentAsync("Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; Googlebot/2.1; +http://www.google.com/bot.html) Chrome/127.0.0.0 Safari/537.36");
                await page.GoToAsync(url, WaitUntilNavigation.Load);
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
                var html = await page.GetContentAsync();
                _memoryCache.Set(cacheKey, html, TimeSpan.FromDays(1));

                await browserPool?.ReturnBrowser(browser);

                return Content(html, "text/html");
            }
        }

        [HttpGet("/screenshot/{width}x{height}/{imagename}/{context}/{*path}")]
        public async Task<IActionResult> GetScreenshot(int width, int height, string context, string imagename, string path, [FromQuery] Dictionary<string, string> queryStringParameters)
        {
            var browser = await browserPool?.GetBrowserAsync();

            var options = new
            {
                Base = $"https://{(context != "general" ? (context + ".") : "")}screenshot.beatleader.xyz/",
                MaxAge = 60 * 60 * 24 * 7,
                Params = HttpUtility.ParseQueryString(""),
                Scale = 1
            };

            foreach (var param in queryStringParameters)
            {
                options.Params[param.Key] = param.Value;
            }

            var cookies = Request.Headers["Cookie"].ToString().Split(';')
                .Select(cookie => cookie.Trim().Split('='))
                .Where(parts => parts.Length >= 2)
                .Select(parts => new CookieParam
                {
                    Name = parts[0],
                    Value = string.Join("=", parts.Skip(1)),
                    Domain = ".beatleader.xyz",
                    Secure = true,
                    SameSite = SameSite.None,
                    Path = "/"
                }).ToList();

            var url = $"{options.Base}{path}?{options.Params}";

            using (var page = await browser.NewPageAsync())
            {
                await page.SetViewportAsync(new ViewPortOptions
                {
                    Width = width,
                    Height = height,
                    DeviceScaleFactor = 1
                });

                foreach (var cookie in cookies)
                {
                    await page.SetCookieAsync(cookie);
                }

                await page.GoToAsync(url, WaitUntilNavigation.Networkidle0);

                var screenshot = await page.ScreenshotDataAsync(new ScreenshotOptions
                {
                    OmitBackground = true
                });

                var maxAge = options.MaxAge;
                if (queryStringParameters.ContainsKey("nocache")) {
                    Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                    Response.Headers["Expires"] = "0";
                } else {
                    Response.Headers["Cache-Control"] = "public, max-age=604800";
                }

                await browserPool?.ReturnBrowser(browser);

                return File(screenshot, "image/png", imagename + ".png");
            }
        }

        [HttpGet("/screenshot/cropped/{width}x{height}/{cropx}x{cropy}x{cropw}x{croph}/{imagename}/{context}/{*path}")]
        public async Task<IActionResult> GetScreenshot(int width, int height, int cropx, int cropy, int cropw, int croph, string context, string imagename, string path, [FromQuery] Dictionary<string, string> queryStringParameters)
        {
            var browser = await browserPool?.GetBrowserAsync();

            var options = new
            {
                Base = $"https://{(context != "general" ? (context + ".") : "")}screenshot.beatleader.xyz/",
                MaxAge = 60 * 60 * 24 * 7,
                Params = HttpUtility.ParseQueryString(""),
                Scale = 1
            };

            foreach (var param in queryStringParameters)
            {
                options.Params[param.Key] = param.Value;
            }

            var cookies = Request.Headers["Cookie"].ToString().Split(';')
                .Select(cookie => cookie.Trim().Split('='))
                .Where(parts => parts.Length >= 2)
                .Select(parts => new CookieParam
                {
                    Name = parts[0],
                    Value = string.Join("=", parts.Skip(1)),
                    Domain = ".beatleader.xyz",
                    Secure = true,
                    SameSite = SameSite.None,
                    Path = "/"
                }).ToList();

            var url = $"{options.Base}{path}?{options.Params}";

            using (var page = await browser.NewPageAsync())
            {
                await page.SetViewportAsync(new ViewPortOptions
                {
                    Width = width,
                    Height = height,
                    DeviceScaleFactor = 1
                });

                foreach (var cookie in cookies)
                {
                    await page.SetCookieAsync(cookie);
                }

                await page.GoToAsync(url, WaitUntilNavigation.Networkidle0);

                var screenshot = await page.ScreenshotDataAsync(new ScreenshotOptions
                {
                    OmitBackground = true
                });

                using (var image = Image.Load(screenshot))
                {
                    var rect = new Rectangle(cropx, cropy, cropw, croph);
                    image.Mutate(ctx => ctx.Crop(rect));

                    using (var ms = new MemoryStream())
                    {
                        await image.SaveAsPngAsync(ms);
                        screenshot = ms.ToArray();
                    }
                }

                var maxAge = options.MaxAge;
                //if (queryStringParameters.ContainsKey("nocache")) {
                    Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                    Response.Headers["Expires"] = "0";
                //} else {
                //    Response.Headers["Cache-Control"] = "public, max-age=604800";
                //}

                await browserPool?.ReturnBrowser(browser);

                return File(screenshot, "image/png", imagename + ".png");
            }
        }

        [NonAction]
        public async Task<byte[]> DownloadFileContent(string context, string path, [FromQuery] Dictionary<string, string> queryStringParameters)
        {
            var browser = await browserPool?.GetBrowserAsync();
            var options = new
            {
                Base = $"https://{(context != "general" ? (context + ".") : "")}screenshot.beatleader.xyz/",
                Params = HttpUtility.ParseQueryString("")
            };

            foreach (var param in queryStringParameters)
            {
                options.Params[param.Key] = param.Value;
            }

            var url = $"{options.Base}{path}?{options.Params}";
            var filename = "clansmap.json";

            using (var page = await browser.NewPageAsync())
            {
                await page.SetViewportAsync(new ViewPortOptions
                {
                    Width = 1024,
                    Height = 768,
                    DeviceScaleFactor = 1,
                });

                if (Request != null) {
                    var cookies = Request.Headers["Cookie"].ToString().Split(';')
                        .Select(cookie => cookie.Trim().Split('='))
                        .Where(parts => parts.Length >= 2)
                        .Select(parts => new CookieParam
                        {
                            Name = parts[0],
                            Value = string.Join("=", parts.Skip(1)),
                            Domain = ".beatleader.xyz",
                            Secure = true,
                            SameSite = SameSite.None,
                            Path = "/"
                        }).ToList();

                    foreach (var cookie in cookies)
                    {
                        await page.SetCookieAsync(cookie);
                    }
                }

                var downloadDirectory = Path.Combine(_webHostEnvironment.WebRootPath, "downloadChrome");
                var filePath = Path.Combine(downloadDirectory, filename);
                if (!Directory.Exists(downloadDirectory)) {
                    Directory.CreateDirectory(downloadDirectory);
                }
                if (System.IO.File.Exists(filePath)) {
                    System.IO.File.Delete(filePath);
                }

                await page.Target.CreateCDPSessionAsync().Result.SendAsync("Page.setDownloadBehavior", new
                {
                    behavior = "allow",
                    downloadPath = downloadDirectory
                }, false);

                await page.GoToAsync(url, WaitUntilNavigation.Networkidle0);

                await Task.Delay(TimeSpan.FromSeconds(30));

                var fileContent = System.IO.File.ReadAllBytes(filePath);

                return fileContent;
            }
        }

        [HttpGet("/download/{context}/{*path}")]
        public async Task<IActionResult> DownloadFile(string context, string path, [FromQuery] Dictionary<string, string> queryStringParameters)
        {
            string filePath = "result.json";
            Response.Headers["Content-Disposition"] = $"attachment; filename={filePath}";
            return File(await DownloadFileContent(context, path, queryStringParameters), "application/octet-stream", filePath);
        }

        [NonAction]
        public async Task<byte[]> DownloadAnimatedScreenshot(
            int width, 
            int height, 
            string context, 
            string path, 
            Dictionary<string, string> queryStringParameters,
            int speed = 1,
            bool loop = false,
            float duration = 2.0f)
        {
            var browser = await browserPool?.GetBrowserAsync();

            var options = new
            {
                Base = $"https://{(context != "general" ? (context + ".") : "")}screenshot.beatleader.xyz/",
                MaxAge = 60 * 60 * 24 * 7,
                Params = HttpUtility.ParseQueryString(""),
                Scale = 1
            };

            foreach (var param in queryStringParameters)
            {
                options.Params[param.Key] = param.Value;
            }

            var url = $"{options.Base}{path}?{options.Params}";

            using (var page = await browser.NewPageAsync())
            {
                await page.SetViewportAsync(new ViewPortOptions
                {
                    Width = width,
                    Height = height,
                    DeviceScaleFactor = 1
                });

                if (Request != null) {
                    var cookies = Request.Headers["Cookie"].ToString().Split(';')
                        .Select(cookie => cookie.Trim().Split('='))
                        .Where(parts => parts.Length >= 2)
                        .Select(parts => new CookieParam
                        {
                            Name = parts[0],
                            Value = string.Join("=", parts.Skip(1)),
                            Domain = ".beatleader.xyz",
                            Secure = true,
                            SameSite = SameSite.None,
                            Path = "/"
                        }).ToList();
                    foreach (var cookie in cookies)
                    {
                        await page.SetCookieAsync(cookie);
                    }
                }

                await page.GoToAsync(url, WaitUntilNavigation.DOMContentLoaded);
                int frameRate = 60;
                int totalFrames = (int)(duration * frameRate);
                var delayBetweenFrames = 1000 / frameRate; 

                List<byte[]> frames = new List<byte[]>();

                await Task.Delay(600);

                for (int i = 0; i < totalFrames; i++)
                {
                    var screenshot = await page.ScreenshotDataAsync(new ScreenshotOptions { OmitBackground = true });
                    if (screenshot.Skip(100).Any(b => b != 0)) {
                        frames.Add(screenshot);
                    }
                    await Task.Delay(delayBetweenFrames / speed);
                }

                var containerImage = Image.Load<Rgba32>(frames[0]);

                foreach (var item in frames)
                {
                    var frame = containerImage.Frames.AddFrame(Image.Load<Rgba32>(item).Frames.RootFrame);
                    frame.Metadata.GetFormatMetadata(GifFormat.Instance).FrameDelay = speed * delayBetweenFrames / 3;
                }
            
                using (MemoryStream ms = new MemoryStream())
                {
                    GifEncoder encoder = new();
                    containerImage.Metadata.GetFormatMetadata(GifFormat.Instance).RepeatCount = (ushort)(loop ? 0 : 1);
                    await encoder.EncodeAsync(containerImage, ms, CancellationToken.None);

                    await browserPool?.ReturnBrowser(browser);

                    return ms.ToArray();
                }
            }
        }

        [HttpGet("/animatedscreenshot/{width}x{height}/{imagename}/{context}/{*path}")]
        public async Task<IActionResult> GetAnimatedScreenshot(int width, int height, string context, string imagename, string path, [FromQuery] Dictionary<string, string> queryStringParameters)
        {
            Response.Headers["Cache-Control"] = "public, max-age=" + 60 * 60 * 24 * 7;
            return File(await DownloadAnimatedScreenshot(width, height, context, path, queryStringParameters), "image/gif", imagename + ".gif");
        }

        [HttpGet("/animatedloop/{width}x{height}/{speed}/{duration}/{imagename}/{context}/{*path}")]
        public async Task<IActionResult> GetAnimatedLoop(int width, int height, int speed, float duration, string context, string imagename, string path, [FromQuery] Dictionary<string, string> queryStringParameters)
        {
            Response.Headers["Cache-Control"] = "public, max-age=" + 60 * 60 * 24 * 7;
            return File(await DownloadAnimatedScreenshot(width, height, context, path, queryStringParameters, speed, true, duration), "image/gif", imagename + ".gif");
        }
    }

    public class BrowserPool
    {
        private readonly int _poolSize;
        private string? exePath = null;
        private readonly Queue<IBrowser> _browsers;
        private readonly object _lock = new object();

        public BrowserPool(int poolSize)
        {
            _poolSize = poolSize;
            _browsers = new Queue<IBrowser>();
        }

        public async Task<IBrowser> GetBrowserAsync()
        {
            lock (_lock)
            {
                if (_browsers.Count > 0)
                {
                    return _browsers.Dequeue();
                }
            }

            if (exePath == null) {
                var options = new BrowserFetcherOptions();
                var bf = new BrowserFetcher(options);
                var installed = await bf.DownloadAsync();   
                exePath = bf.GetExecutablePath(installed.BuildId);
            }

            return await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Args = new string[]
                {
                    "--autoplay-policy=user-gesture-required",
                    "--disable-background-networking",
                    "--disable-background-timer-throttling",
                    "--disable-backgrounding-occluded-windows",
                    "--disable-breakpad",
                    "--disable-client-side-phishing-detection",
                    "--disable-component-update",
                    "--disable-default-apps",
                    "--disable-dev-shm-usage",
                    "--disable-domain-reliability",
                    "--disable-extensions",
                    "--disable-features=AudioServiceOutOfProcess",
                    "--disable-hang-monitor",
                    "--disable-ipc-flooding-protection",
                    "--disable-notifications",
                    "--disable-offer-store-unmasked-wallet-cards",
                    "--disable-popup-blocking",
                    "--disable-print-preview",
                    "--disable-prompt-on-repost",
                    "--disable-renderer-backgrounding",
                    "--disable-setuid-sandbox",
                    "--disable-speech-api",
                    "--disable-sync",
                    "--hide-scrollbars",
                    "--ignore-gpu-blacklist",
                    "--metrics-recording-only",
                    "--mute-audio",
                    "--no-default-browser-check",
                    "--no-first-run",
                    "--no-pings",
                    "--no-sandbox",
                    "--no-zygote",
                    "--password-store=basic",
                    "--use-gl=swiftshader",
                    "--use-mock-keychain",
                    "--disable-application-cache",
                    "--disable-offline-load-stale-cache",
                    "--disable-gpu-shader-disk-cache",
                    "--disable-component-extensions-with-background-pages",
                },
                Headless = true,
                ExecutablePath = exePath,
            });
        }

        public async Task ReturnBrowser(IBrowser browser)
        {
            bool shouldClose = false;
            lock (_lock)
            {
                if (_browsers.Count < _poolSize)
                {
                    _browsers.Enqueue(browser);
                }
                else
                {
                    shouldClose = true;
                }
            }

            if (shouldClose) {
                await browser.CloseAsync();
                browser.Dispose();
            }
        }
    }
}

