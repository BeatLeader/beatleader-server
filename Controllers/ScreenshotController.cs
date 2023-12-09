
using Microsoft.AspNetCore.Mvc;
using PuppeteerSharp;
using System.Web;

namespace BeatLeader_Server.Controllers
{
    public class ScreenshotController : Controller
    {
        private static BrowserPool? browserPool;
        private readonly IConfiguration configuration;

        public ScreenshotController(IConfiguration configuration)
        {
            this.configuration = configuration;
            if (browserPool == null) {
                browserPool = new BrowserPool(5);
            }
        }

        [HttpGet("/screenshot/{width}x{height}/{imagename}/{*path}")]
        public async Task<IActionResult> GetScreenshot(int width, int height, string path, string imagename, [FromQuery] Dictionary<string, string> queryStringParameters)
        {
            var browser = await browserPool?.GetBrowserAsync();

            var options = new
            {
                Base = "https://screenshot.beatleader.xyz/",
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
                Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                Response.Headers["Expires"] = "0";

                browserPool?.ReturnBrowser(browser);

                return File(screenshot, "image/png", imagename + ".png");
            }
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
                const string ChromiumRevision = "120.0.6099.71";
                var options = new BrowserFetcherOptions();
                var bf = new BrowserFetcher(options);
                await bf.DownloadAsync(ChromiumRevision);   
                exePath = bf.GetExecutablePath(ChromiumRevision);
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
                    "--use-mock-keychain"
                },
                Headless = true,
                ExecutablePath = exePath,
            });
        }

        public void ReturnBrowser(IBrowser browser)
        {
            lock (_lock)
            {
                if (_browsers.Count < _poolSize)
                {
                    _browsers.Enqueue(browser);
                }
                else
                {
                    browser.Dispose();
                }
            }
        }
    }
}

