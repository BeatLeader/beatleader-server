using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace BeatLeader_Server.Utils;

public class ImageUtils
{
    public static (string, MemoryStream) GetFormatAndResize(MemoryStream memoryStream)
    {
        Image image = Image.Load(memoryStream, out IImageFormat format);
        Size size = image.Size();

        int width = Math.Min(200, size.Width);
        int height = (int)(size.Height / (float)size.Width * width);

        if (width < height)
        {
            image.Mutate(imageProcessingContext => imageProcessingContext.Resize(width, height).Crop(new Rectangle(0, (height / 2) - (width / 2), width, width)));
        }
        else if (width > height)
        {
            image.Mutate(imageProcessingContext => imageProcessingContext.Resize(width, height).Crop(new Rectangle((width / 2) - (height / 2), 0, height, height)));
        }
        else
        {
            image.Mutate(imageProcessingContext => imageProcessingContext.Resize(width, height));
        }

        MemoryStream ms = new(5);
        string extension;

        if (format.Name == "GIF")
        {
            image.SaveAsGif(ms);
            extension = ".gif";
        }
        else
        {
            WebpEncoder webpEncoder = new()
            {
                NearLossless = true,
                NearLosslessQuality = 80,
                TransparentColorMode = WebpTransparentColorMode.Preserve,
                Quality = 20,
            };

            image.SaveAsWebp(ms, webpEncoder);
            extension = ".webp";
        }

        ms.Position = 0;

        return (extension, ms);
    }

    public static (string, MemoryStream) GetFormat(MemoryStream memoryStream)
    {
        Image image = Image.Load(memoryStream, out IImageFormat format);

        MemoryStream ms = new(5);
        string extension;

        if (format.Name == "GIF")
        {
            image.SaveAsGif(ms);
            extension = ".gif";
        }
        else
        {
            WebpEncoder webpEncoder = new()
            {
                NearLossless = true,
                NearLosslessQuality = 80,
                TransparentColorMode = WebpTransparentColorMode.Preserve,
                Quality = 20,
            };

            image.SaveAsWebp(ms, webpEncoder);
            extension = ".webp";
        }

        ms.Position = 0;

        return (extension, ms);
    }
}