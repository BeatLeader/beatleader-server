using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Newtonsoft.Json;
using ReplayDecoder;

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
        unicode,
        configs,
        replayedvalues,
        reepresets
    }

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

		public static async Task<string> UploadStream(this IAmazonS3 client, string filename, S3Container container, Stream data, bool closeStream = true)
        {
            try {
	            var request = new PutObjectRequest
	            {
                    InputStream = data,
		            Key = filename,
		            BucketName = container.ToString(),
		            DisablePayloadSigning = true,
                    AutoCloseStream = false
	            };
            
	            await client.PutObjectAsync(request);

                if (closeStream) {
                    data.Close();
                }

                return $"https://cdn.{container}.beatleader.xyz/{filename}";
            } catch (Exception e) {
                string directoryPath = Path.Combine("/root", container.ToString());
                string filePath = Path.Combine(directoryPath, filename);

                // Ensure the directory exists.
                Directory.CreateDirectory(directoryPath);

                data.Position = 0;

                // Use FileMode.Create to overwrite the file if it already exists.
                using (FileStream fs = new FileStream(filePath, FileMode.Create))
                {
                    await data.CopyToAsync(fs);
                }

                return $"{MinuteRefresh.CurrentHost}backup/file/{container}/{filename}";
            }
        }
		
        public static async Task<string> UploadReplay(this IAmazonS3 client, string filename, byte[] data)
        {   
	        return await client.UploadStream(filename, S3Container.replays, new BinaryData(data).ToStream());
        }

        public static async Task<string> UploadAsset(this IAmazonS3 client, string filename, byte[] data)
        {   
	        return await client.UploadStream(filename, S3Container.assets, new BinaryData(data).ToStream());
        }

        public static async Task<string> UploadOtherReplay(this IAmazonS3 client, string filename, byte[] data)
        {   
	        return await client.UploadStream(filename, S3Container.otherreplays, new BinaryData(data).ToStream());
        }

        public static async Task<string> UploadOtherReplayStream(this IAmazonS3 client, string filename, Stream data)
        {   
	        return await client.UploadStream(filename, S3Container.otherreplays, data);
        }

        public static async Task<string> UploadAsset(this IAmazonS3 client, string filename, Stream data)
        {
            return await client.UploadStream(filename, S3Container.assets, data);
        }

        public static async Task<string> UploadReepreset(this IAmazonS3 client, string filename, Stream data)
        {
            return await client.UploadStream(filename, S3Container.reepresets, data);
        }

		public static async Task<string> UploadPreview(this IAmazonS3 client, string filename, Stream data)
        {
			return await client.UploadStream(filename, S3Container.previews, data, false);
        }

		public static async Task<string> UploadScoreStats(this IAmazonS3 client, string filename, ScoreStatistic scoreStats)
        {
            return await client.UploadStream(filename, S3Container.scorestats, new BinaryData(JsonConvert.SerializeObject(scoreStats)).ToStream());
        }

        public static async Task<string> UploadPlaylist(IAmazonS3 client, string filename, dynamic playlist)
        {
            return await client.UploadStream(filename, S3Container.playlists, new BinaryData(JsonConvert.SerializeObject(playlist)).ToStream());
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
