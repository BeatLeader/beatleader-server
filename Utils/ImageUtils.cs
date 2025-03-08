using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace BeatLeader_Server.Utils
{
    public class ImageUtils
    {
        public static (string, MemoryStream) GetFormatAndResize(MemoryStream memoryStream)
        {
            IImageFormat format = Image.DetectFormat(memoryStream);
            Image image = Image.Load(memoryStream);
            
            Size size = image.Size;

            int width = Math.Min(200, size.Width);
            int height = (int)(((float)size.Height / (float)size.Width) * (float)width);

            if (width < height) {
                image.Mutate(i => i.Resize(width, height).Crop(new Rectangle(0, height / 2 - width / 2, width, width)));
            } else if (width > height) {
                image.Mutate(i => i.Resize(width, height).Crop(new Rectangle(width / 2 - height / 2, 0, height, height)));
            } else {
                image.Mutate(i => i.Resize(width, height));
            }

            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;

            var ms = new MemoryStream(5);
            string extension;

            if (format.Name == "GIF") {
                image.SaveAsGif(ms);
                extension = ".gif";
            } else {
                image.SaveAsPng(ms);
                extension = ".png";
            }
            ms.Position = 0;

            return (extension, ms);
        }

        public static MemoryStream ResizeToWebp(MemoryStream memoryStream, int desiredWidth = 300)
        {
            Image image = Image.Load(memoryStream);
            Size size = image.Size;

            int width = Math.Min(desiredWidth, size.Width);
            int height = (int)(((float)size.Height / (float)size.Width) * (float)width);

            if (width < height) {
                image.Mutate(i => i.Resize(width, height).Crop(new Rectangle(0, height / 2 - width / 2, width, width)));
            } else if (width > height) {
                image.Mutate(i => i.Resize(width, height).Crop(new Rectangle(width / 2 - height / 2, 0, height, height)));
            } else {
                image.Mutate(i => i.Resize(width, height));
            }

            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;

            var ms = new MemoryStream(5);
            image.SaveAsWebp(ms);
            ms.Position = 0;

            return ms;
        }
        
        public static (string, MemoryStream) GetFormat(MemoryStream memoryStream)
        {
            IImageFormat format = Image.DetectFormat(memoryStream);
            Image image = Image.Load(memoryStream);

            var ms = new MemoryStream(5);
            string extension;

            if (format.Name == "GIF") {
                image.SaveAsGif(ms);
                extension = ".gif";
            } else {
                WebpEncoder webpEncoder = new()
                {
                    NearLossless = true,
                    NearLosslessQuality = 80,
                    TransparentColorMode = WebpTransparentColorMode.Preserve,
                    Quality = 75,
                };

                image.Mutate(x => x.AutoOrient());
                image.Metadata.ExifProfile = null;
                image.Metadata.XmpProfile = null;

                image.SaveAsWebp(ms, webpEncoder);
                extension = ".webp";
            }
            ms.Position = 0;

            return (extension, ms);
        }

        public static (string, MemoryStream) GetFormatPng(MemoryStream memoryStream)
        {
            IImageFormat format = Image.DetectFormat(memoryStream);
            Image image = Image.Load(memoryStream);

            var ms = new MemoryStream(5);
            string extension;

            if (format.Name == "GIF") {
                image.SaveAsGif(ms);
                extension = ".gif";
            } else {
                image.SaveAsPng(ms);
                extension = ".png";
            }
            ms.Position = 0;

            return (extension, ms);
        }
    }
}
