using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BeatLeader_Server.Models;
using Newtonsoft.Json;

namespace BeatLeader_Server.Utils
{
    public enum S3Container
    {
        replays,
        otherreplays,
        assets,
        previews,
        scorestats,
        playlists,
        unicode
    }

    public static class S3Helper
    {
        public static IAmazonS3 GetS3Client(this IConfiguration config) {
            var credentials = new BasicAWSCredentials(
            config.GetValue<string>("S3AccessKey"), 
            config.GetValue<string>("S3AccessSecret"));
            return new AmazonS3Client(credentials, new AmazonS3Config
            {
                // TODO: CHANGE BACK BEFORE PROD
                RegionEndpoint = RegionEndpoint.USEast2
                //ServiceURL = "https://" + config.GetValue<string>("S3AccountID") + ".r2.cloudflarestorage.com",
                //ServiceURL = "https://0eabd091b5ad7e4a48bac26d0ed8a127.r2.cloudflarestorage.com/ssnowy-beatleader-testing",
            });
        }

        public static async Task UploadStream(this IAmazonS3 client, string filename, S3Container container, Stream data, bool closeStream = true)
        {
            try {
                var request = new PutObjectRequest
                {
                    InputStream = data,
                    Key = filename,
                    // TODO: CHANGE BACK BEFORE PROD
                    BucketName = "ssnowy-beatleader-testing",
                    DisablePayloadSigning = true,
                    AutoCloseStream = closeStream
                };
            
                await client.PutObjectAsync(request);
            } catch (Exception _) {
                using (FileStream fs = File.Create("/root/" + container.ToString() + "/" + filename))
                {
                    await data.CopyToAsync(fs);
                }
            }
        }
        
        public static async Task UploadReplay(this IAmazonS3 client, string filename, byte[] data)
        {   
            await client.UploadStream(filename, S3Container.replays, new BinaryData(data).ToStream());
        }

        public static async Task UploadOtherReplay(this IAmazonS3 client, string filename, byte[] data)
        {   
            await client.UploadStream(filename, S3Container.otherreplays, new BinaryData(data).ToStream());
        }

        public static async Task UploadOtherReplayStream(this IAmazonS3 client, string filename, Stream data)
        {   
            await client.UploadStream(filename, S3Container.otherreplays, data);
        }

        public static async Task UploadAsset(this IAmazonS3 client, string filename, Stream data)
        {
            await client.UploadStream(filename, S3Container.assets, data);
        }

        public static async Task UploadPreview(this IAmazonS3 client, string filename, Stream data)
        {
            await client.UploadStream(filename, S3Container.previews, data, false);
        }

        public static async Task UploadScoreStats(this IAmazonS3 client, string filename, ScoreStatistic scoreStats)
        {
            await client.UploadStream(filename, S3Container.scorestats, new BinaryData(JsonConvert.SerializeObject(scoreStats)).ToStream());
        }

        public static async Task UploadPlaylist(IAmazonS3 client, string filename, dynamic playlist)
        {
            await client.UploadStream(filename, S3Container.playlists, new BinaryData(JsonConvert.SerializeObject(playlist)).ToStream());
        }

        public static async Task<Stream?> DownloadStreamOffset(
            this IAmazonS3 client, 
            string filename, 
            S3Container container,
            int offset,
            int size) {
            var request = new GetObjectRequest 
            {
                BucketName = container.ToString(),
                Key = filename,
                ByteRange = new ByteRange(offset, offset + size)
            };

            try
            {
                var result = await client.GetObjectAsync(request);
            
                return result.HttpStatusCode == System.Net.HttpStatusCode.OK || result.HttpStatusCode == System.Net.HttpStatusCode.PartialContent ? result.ResponseStream : null;
            } catch (Exception ex) {
                return null;
            }
        }

        public static async Task<Stream?> DownloadStream(this IAmazonS3 client, string filename, S3Container container)
        {
            try
            {
                var result = await client.GetObjectAsync(container.ToString(), filename);
            
                return result.HttpStatusCode == System.Net.HttpStatusCode.OK ? result.ResponseStream : null;
            } catch (Exception ex) {
                return null;
            }
        }

        public static async Task<Stream?> DownloadReplay(this IAmazonS3 client, string filename)
        {
            return await client.DownloadStream(filename, S3Container.replays);
        }

        public static async Task<Stream?> DownloadOtherReplay(this IAmazonS3 client, string filename)
        {
            return await client.DownloadStream(filename, S3Container.otherreplays);
        }

        public static async Task<Stream?> DownloadAsset(this IAmazonS3 client, string filename)
        {
            return await client.DownloadStream(filename, S3Container.assets);
        }

        public static async Task<Stream?> DownloadPreview(this IAmazonS3 client, string filename)
        {
            return await client.DownloadStream(filename, S3Container.previews);
        }

        public static async Task<Stream?> DownloadPlaylist(this IAmazonS3 client, string filename)
        {
            return await client.DownloadStream(filename, S3Container.playlists);
        }

        public static async Task<Stream?> DownloadStats(this IAmazonS3 client, string filename)
        {
            return await client.DownloadStream(filename, S3Container.scorestats);
        }

        public static async Task DeleteFile(this IAmazonS3 client, string filename, S3Container container)
        {
            var request = new DeleteObjectRequest
            {
                Key = filename,
                BucketName = container.ToString()
            };
            
            try {
            await client.DeleteObjectAsync(request);
            } catch (Exception ex) { }
        }

        public static async Task DeleteStats(this IAmazonS3 client, string filename) {
            await client.DeleteFile(filename, S3Container.scorestats);
        }

        public static async Task DeleteReplay(this IAmazonS3 client, string filename) {
            await client.DeleteFile(filename, S3Container.replays);
        }
    }
}
