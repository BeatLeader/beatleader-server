using System;
using System.Linq;
using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.AspNetCore.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class UnicodeController : Controller
    {
        private readonly BlobContainerClient _unicodeClient;

        public UnicodeController(
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                _unicodeClient = new BlobContainerClient(config.Value.AccountName, config.Value.UnicodeContainerName);
            }
            else
            {
                string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        config.Value.AccountName,
                                                       config.Value.UnicodeContainerName);

                _unicodeClient = new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential());
            }
        }

        [HttpGet("~/unicode/{name}")]
        public async Task<ActionResult> GetUnicode(string name)
        {
            var blob = _unicodeClient.GetBlobClient(name);
            return (await blob.ExistsAsync()) ? File(await blob.OpenReadAsync(), "image/png") : NotFound();
        }
    }
}
