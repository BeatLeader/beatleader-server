using System;
using System.Net.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace BLHistories
{
    public static class BLHistories
    {
        [FunctionName("BLHistories")]
        public static void Run([TimerTrigger("0 0 * * *")]TimerInfo myTimer, ILogger log)
        {
            var url = "https://api.beatleader.xyz/players/sethistories";
            HttpClient Client = new HttpClient();
            Client.GetAsync(url);
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
        }
    }
}

