using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BeatLeader_Server.Models;
using Newtonsoft.Json;

namespace BeatLeader_Server.Utils
{
    public static class S3Helper
    {
		public static IAmazonS3 GetS3Client(this IConfiguration config) {
			var credentials = new BasicAWSCredentials(
            config.GetValue<string>("S3AccessKey"), 
            config.GetValue<string>("S3AccessSecret"));
            return new AmazonS3Client(credentials, new AmazonS3Config
		    {
			    ServiceURL = "https://" + config.GetValue<string>("S3AccountID") + ".r2.cloudflarestorage.com",
		    });
		}

		public static async Task UploadStream(this IAmazonS3 client, string filename, string container, Stream data, bool closeStream = true)
        {
	        var request = new PutObjectRequest
	        {
                InputStream = data,
		        Key = filename,
		        BucketName = container,
		        DisablePayloadSigning = true,
                AutoCloseStream = closeStream
	        };
            
	        await client.PutObjectAsync(request);
        }
		
        public static async Task UploadReplay(this IAmazonS3 client, string filename, byte[] data)
        {   
	        await client.UploadStream(filename, "replays", new BinaryData(data).ToStream());
        }

        public static async Task UploadAsset(this IAmazonS3 client, string filename, Stream data)
        {
            await client.UploadStream(filename, "assets", data);
        }

		public static async Task UploadPreview(this IAmazonS3 client, string filename, Stream data)
        {
			await client.UploadStream(filename, "previews", data, false);
        }

		public static async Task UploadScoreStats(this IAmazonS3 client, string filename, ScoreStatistic scoreStats)
        {
            await client.UploadStream(filename, "scorestats", new BinaryData(JsonConvert.SerializeObject(scoreStats)).ToStream());
        }

        public static async Task UploadPlaylist(IAmazonS3 client, string filename, dynamic playlist)
        {
            await client.UploadStream(filename, "playlists", new BinaryData(JsonConvert.SerializeObject(playlist)).ToStream());
        }

        public static async Task<Stream?> DownloadStream(this IAmazonS3 client, string filename, string container)
        {
            try
            {
			    var result = await client.GetObjectAsync(container, filename);
            
	            return result.HttpStatusCode == System.Net.HttpStatusCode.OK ? result.ResponseStream : null;
            } catch (Exception ex) {
                return null;
            }
        }

        public static async Task<Stream?> DownloadReplay(this IAmazonS3 client, string filename)
        {
            return await client.DownloadStream(filename, "replays");
        }

		public static async Task<Stream?> DownloadAsset(this IAmazonS3 client, string filename)
        {
            return await client.DownloadStream(filename, "assets");
        }

		public static async Task<Stream?> DownloadPreview(this IAmazonS3 client, string filename)
        {
            return await client.DownloadStream(filename, "previews");
        }

        public static async Task<Stream?> DownloadPlaylist(this IAmazonS3 client, string filename)
        {
            return await client.DownloadStream(filename, "playlists");
        }
    }
}
