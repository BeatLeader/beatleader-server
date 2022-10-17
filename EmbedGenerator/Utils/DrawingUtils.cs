using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Text;
using ImageProcessor;
using ImageProcessor.Imaging;

internal static class DrawingUtils {
    #region OverlayRegion

    public static ImageFactory OverlayRegion(this ImageFactory imageFactory, Image source, ResizeMode resizeMode = ResizeMode.Stretch) {
        return imageFactory.OverlayRegion(source, Point.Empty, imageFactory.Image.Size, resizeMode);
    }

    public static ImageFactory OverlayRegion(this ImageFactory imageFactory, Image source, Size size, ResizeMode resizeMode = ResizeMode.Stretch) {
        return imageFactory.OverlayRegion(source, Point.Empty, size, resizeMode);
    }

    public static ImageFactory OverlayRegion(this ImageFactory imageFactory, Image source, Rectangle rect, ResizeMode resizeMode = ResizeMode.Stretch) {
        return imageFactory.OverlayRegion(source, rect.Location, rect.Size, resizeMode);
    }

    public static ImageFactory OverlayRegion(this ImageFactory imageFactory, Image source, Point position, Size size, ResizeMode resizeMode = ResizeMode.Stretch) {
        var imageLayer = new ImageLayer() {
            Image = source.ResizeIfNecessary(size, resizeMode),
            Position = position
        };

        return imageFactory.Overlay(imageLayer);
    }

    #endregion

    #region MaskRegion

    public static ImageFactory MaskRegion(this ImageFactory imageFactory, Image mask, ResizeMode resizeMode = ResizeMode.Stretch) {
        return imageFactory.MaskRegion(mask, Point.Empty, imageFactory.Image.Size, resizeMode);
    }

    public static ImageFactory MaskRegion(this ImageFactory imageFactory, Image mask, Size size, ResizeMode resizeMode = ResizeMode.Stretch) {
        return imageFactory.MaskRegion(mask, Point.Empty, size, resizeMode);
    }

    public static ImageFactory MaskRegion(this ImageFactory imageFactory, Image mask, Rectangle rect, ResizeMode resizeMode = ResizeMode.Stretch) {
        return imageFactory.MaskRegion(mask, rect.Location, rect.Size, resizeMode);
    }

    public static ImageFactory MaskRegion(this ImageFactory imageFactory, Image mask, Point position, Size size, ResizeMode resizeMode = ResizeMode.Stretch) {
        var imageLayer = new ImageLayer() {
            Image = mask.ResizeIfNecessary(size, resizeMode),
            Position = position
        };

        return imageFactory.Mask(imageLayer);
    }

    #endregion

    #region ResizeIfNecessary

    public static Image ResizeIfNecessary(this Image source, Size targetSize, ResizeMode resizeMode = ResizeMode.Stretch) {
        if (source.Size == targetSize) return source;

        return new ImageFactory()
            .Load(source)
            .Resize(new ResizeLayer(targetSize, resizeMode))
            .Image;
    }

    #endregion

    #region DrawTextCentered

    public static void DrawTextCentered(
        this Graphics graphics,
        string text,
        Font font,
        Color color,
        Rectangle rectangle
    ) {
        var fittingSize = graphics.MeasureString(text, font);
        var point = new PointF(
            rectangle.Location.X + (rectangle.Width - fittingSize.Width) / 2,
            rectangle.Location.Y + (rectangle.Height - fittingSize.Height * 0.8f) / 2
        );
        graphics.DrawString(text, font, new SolidBrush(color), point);
    }

    #endregion

    #region FitText

    public static void FitText(
        this Graphics graphics,
        string text,
        Color color,
        FontFamily fontFamily,
        Rectangle rectangle,
        float minimalFontSize = 0.0f
    ) {
        var canFit = graphics.TryGetFittingFont(text, fontFamily, rectangle, 40, out var fittingFont, out var fittingSize);
        if (!canFit || fittingFont!.Size < minimalFontSize) {
            fittingFont = new Font(fontFamily, minimalFontSize);
            graphics.TryGetFittingString(text, fittingFont, rectangle, out text, out fittingSize);
            if (graphics.TryGetFittingFont(text, fontFamily, rectangle, 40, out var finalFont, out var finalSize)) {
                fittingFont = finalFont;
                fittingSize = finalSize;
            }
        }

        var point = new PointF(
            rectangle.Location.X + (rectangle.Width - fittingSize.Width) / 2,
            rectangle.Location.Y + (rectangle.Height - fittingSize.Height * 0.8f) / 2
        );
        graphics.DrawString(text, fittingFont, new SolidBrush(color), point);
    }

    private static bool TryGetFittingString(
        this Graphics graphics,
        string text,
        Font font,
        Rectangle rectangle,
        out string result,
        out SizeF measuredSize
    ) {
        var sb = new StringBuilder();

        var words = text.Split(" ");
        for (var i = words.Length - 1; i > 2; i--) {
            sb.Clear();

            for (var j = 0; j < i; j++) {
                if (j > 0) sb.Append(' ');
                sb.Append(words[j]);
            }

            result = $"{sb}...";
            measuredSize = graphics.MeasureString(result, font);
            if (measuredSize.Width < rectangle.Size.Width) return true;
        }

        var chars = text.ToCharArray();
        for (var i = chars.Length - 1; i > 0; i--) {
            result = $"{text[..i]}...";
            measuredSize = graphics.MeasureString(result, font);
            if (measuredSize.Width < rectangle.Size.Width) return true;
        }

        result = string.Empty;
        measuredSize = SizeF.Empty;
        return false;
    }

    private static bool TryGetFittingFont(
        this Graphics graphics,
        string text,
        FontFamily fontFamily,
        Rectangle rectangle,
        int steps,
        out Font? font,
        out SizeF measuredSize
    ) {
        var fontSize = (float)rectangle.Height;
        var decPerStep = fontSize / steps;

        while (fontSize > 0) {
            font = new Font(fontFamily, fontSize);
            measuredSize = graphics.MeasureString(text, font);
            if (measuredSize.Width < rectangle.Width) return true;
            fontSize -= decPerStep;
        }

        font = null;
        measuredSize = SizeF.Empty;
        return false;
    }

    #endregion

    #region ApplyHsbTransform

    public static Image ApplyHsbTransform(this Image source, float hueShiftDegrees, float saturation, float brightness) {
        var hueShiftRadians = hueShiftDegrees * MathF.PI / 180;

        var width = source.Width;
        var height = source.Height;

        var destination = new Bitmap(width, height, PixelFormat.Format32bppPArgb);

        using (var sourceBitmap = new FastBitmap(source)) {
            using (var destinationBitmap = new FastBitmap(destination)) {
                Parallel.For(0, height, y => {
                    for (var x = 0; x < width; x += 1) {
                        var col = FloatColor.FromColor(sourceBitmap.GetPixel(x, y));
                        col.ApplyHsbTransform(hueShiftRadians, saturation, brightness);
                        destinationBitmap.SetPixel(x, y, col.ToColor());
                    }
                });
            }
        }

        return destination;
    }

    #endregion

    #region DrawGradient

    public static void DrawGradient(Image destination, Color fromColor, Color toColor, Vector2 from, Vector2 to) {
        var width = destination.Width;
        var height = destination.Height;

        var diff = to - from;
        var direction = Vector2.Normalize(diff);
        var magnitude = MathF.Sqrt(diff.X * diff.X + diff.Y * diff.Y);

        var fromFloatColor = FloatColor.FromColor(fromColor);
        var toFloatColor = FloatColor.FromColor(toColor);

        using (var destinationBitmap = new FastBitmap(destination)) {
            Parallel.For(0, height, (int y) => {
                for (var x = 0; x < width; x += 1) {
                    var t = Vector2.Dot(new Vector2(x - from.X, y - from.Y), direction) / magnitude;
                    var col = FloatColor.LerpClamped(fromFloatColor, toFloatColor, t);
                    destinationBitmap.SetPixel(x, y, col.ToColor());
                }
            });
        }
    }

    #endregion

    #region RectangleUtils

    public static Rectangle CenteredRectangle(PointF center, SizeF size) {
        return Rectangle.Round(new RectangleF(new PointF(center.X - size.Width / 2, center.Y - size.Height / 2), size));
    }

    public static Rectangle CenteredRectangle(Point center, Size size) {
        return new Rectangle(new Point(center.X - size.Width / 2, center.Y - size.Height / 2), size);
    }

    #endregion
}