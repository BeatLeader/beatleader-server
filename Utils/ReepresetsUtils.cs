using BeatLeader_Server.Models;
using Newtonsoft.Json;
using System.IO.Compression;

namespace BeatLeader_Server.Utils
{
    public class ReepresetsUtils
    {
        public static async Task<MemoryStream> QuestMod(ReeSabersPreset preset, HttpClient httpClient) {
            var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                await AddFilesToArchiveQuest(preset, archive, httpClient);

                var modJson = CreateModJson(preset);
                var modJsonEntry = archive.CreateEntry("mod.json");
                using var entryStream = modJsonEntry.Open();
                using var streamWriter = new StreamWriter(entryStream);
                await streamWriter.WriteAsync(modJson);
            }

            return memoryStream;
        }

        public static async Task<MemoryStream> PCMod(ReeSabersPreset preset, HttpClient httpClient) {
            var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                await AddFilesToArchivePC(preset, archive, httpClient);
            }

            return memoryStream;
        }

        public static string GetFileName(string url) {
            return Path.GetFileName(url.Split("_")[3]);
        }

        private static async Task AddFilesToArchivePC(ReeSabersPreset preset, ZipArchive archive, HttpClient httpClient)
        {
            foreach (var link in preset.JsonLinks.Split(','))
            {
                var response = await httpClient.GetAsync(link);
                if (response.IsSuccessStatusCode)
                {
                    var entry = archive.CreateEntry("UserData/ReeSabers/Presets/" + GetFileName(link));
                    using var entryStream = entry.Open();
                    await response.Content.CopyToAsync(entryStream);
                }
            }

            if (string.IsNullOrEmpty(preset.TextureLinks)) {
                foreach (var link in preset.TextureLinks.Split(','))
                {
                    var response = await httpClient.GetAsync(link);
                    if (response.IsSuccessStatusCode)
                    {
                        var entry = archive.CreateEntry("UserData/ReeSabers/CustomTextures/" + GetFileName(link));
                        using var entryStream = entry.Open();
                        await response.Content.CopyToAsync(entryStream);
                    }
                }
            }
        }

        private static async Task AddFilesToArchiveQuest(ReeSabersPreset preset, ZipArchive archive, HttpClient httpClient)
        {
            foreach (var link in (preset.JsonLinks + (string.IsNullOrEmpty(preset.TextureLinks) ? "" : "," + preset.TextureLinks)).Split(','))
            {
                var response = await httpClient.GetAsync(link);
                if (response.IsSuccessStatusCode)
                {
                    var entry = archive.CreateEntry(GetFileName(link));
                    using var entryStream = entry.Open();
                    await response.Content.CopyToAsync(entryStream);
                }
            }

            if (!string.IsNullOrEmpty(preset.CoverLink))
            {
                var coverResponse = await httpClient.GetAsync(preset.CoverLink);
                if (coverResponse.IsSuccessStatusCode)
                {
                    var coverEntry = archive.CreateEntry(GetFileName(preset.CoverLink));
                    using var entryStream = coverEntry.Open();
                    await coverResponse.Content.CopyToAsync(entryStream);
                }
            }
        }

        private static string CreateModJson(ReeSabersPreset preset)
        {
            var modJsonObj = new
            {
                _QPVersion = "0.1.1",
                name = preset.Name,
                id = "rs_1699378969025",
                author = preset.Owner.Name,
                version = preset.Version,
                packageId = "com.beatgames.beatsaber",
                packageVersion = "1.28.0_4124311467",
                description = "Generated QMod for automatic ReeSaber asset copying",
                coverImage = GetFileName(preset.CoverLink),
                dependencies = new string[] { },
                fileCopies = preset.JsonLinks.Split(',').Select(link => new
                {
                    name = GetFileName(link),
                    destination = $"sdcard/ModData/com.beatgames.beatsaber/Mods/ReeSabers/Presets/{GetFileName(link)}"
                }).ToArray(),
                copyExtensions = new string[] { }
            };

            return JsonConvert.SerializeObject(modJsonObj);
        }
    }
}
