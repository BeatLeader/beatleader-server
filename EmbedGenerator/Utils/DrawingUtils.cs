using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text;

internal static class DrawingUtils {
    #region Resized

    public static Image<Rgba32> Resized(this Image<Rgba32> source, Size newSize)
    {
        if (newSize == source.Size) return source;

        var resizedImage = source.Clone();
        resizedImage.Mutate(x => x.Resize(newSize.Width, newSize.Height));
        return resizedImage;
    }

    public static SizeF MeasureString(string value, Font font) {
        var options = new TextOptions(font);
        options.Dpi = 96;
        var measurement = TextMeasurer.Measure(value, options);
        return new SizeF(measurement.Width, measurement.Height);
    }

    #endregion

    #region DrawTextCentered

    public static void DrawTextCentered(
        this Image<Rgba32> image,
        string text,
        Font font,
        IReadOnlyList<FontFamily> fallbackFamilies,
        Color color,
        Rectangle rectangle)
    {
        var measurement = MeasureString(text, font);

        var point = new PointF(
            rectangle.Location.X + (rectangle.Width - measurement.Width) / 2,
            rectangle.Location.Y + (rectangle.Height - measurement.Height * 0.8f) / 2
        );

        var options = new TextOptions(font);
        options.Dpi = 96;
        options.Origin = point;
        options.FallbackFontFamilies = fallbackFamilies;
        options.TextAlignment = TextAlignment.Center;

        image.Mutate(x => x.DrawText(options, text, color));
    }

    #endregion

    #region FitText

    public static void FitText(
        this Image<Rgba32> image,
        string text,
        Color color,
        FontFamily fontFamily,
        IReadOnlyList<FontFamily> fallbackFamilies,
        Rectangle rectangle,
        float minimalFontSize = 0.0f
    )
    {
        var canFit = TryGetFittingFont(text, fontFamily, rectangle, 40, out var fittingFont, out var fittingSize);
        if (!canFit || fittingFont!.Size < minimalFontSize)
        {
            fittingFont = new Font(fontFamily, minimalFontSize);
            TryGetFittingString(text, fittingFont, rectangle, out text, out fittingSize);
            if (TryGetFittingFont(text, fontFamily, rectangle, 40, out var finalFont, out var finalSize))
            {
                fittingFont = finalFont;
                fittingSize = finalSize;
            }
        }

        var point = new PointF(
            rectangle.Location.X + rectangle.Width / 2,
            rectangle.Location.Y + rectangle.Height / 2
        );

        var options = new TextOptions(fittingFont);
        options.Dpi = 96;
        options.Origin = point;
        options.FallbackFontFamilies = fallbackFamilies;

        options.HorizontalAlignment = HorizontalAlignment.Center;
        options.VerticalAlignment = VerticalAlignment.Center;
        options.TextAlignment = TextAlignment.Center;

        image.Mutate(x => x.DrawText(options, text, color));
    }

    private static bool TryGetFittingString(
        string text,
        Font font,
        Rectangle rectangle,
        out string result,
        out SizeF measuredSize
    )
    {
        var sb = new StringBuilder();

        var words = text.Split(" ");
        for (var i = words.Length - 1; i > 2; i--)
        {
            sb.Clear();

            for (var j = 0; j < i; j++)
            {
                if (j > 0) sb.Append(' ');
                sb.Append(words[j]);
            }

            result = $"{sb}...";
            measuredSize = MeasureString(result, font);
            if (measuredSize.Width < rectangle.Size.Width) return true;
        }

        var chars = text.ToCharArray();
        for (var i = chars.Length - 1; i > 0; i--)
        {
            result = $"{text[..i]}...";
            measuredSize = MeasureString(result, font);
            if (measuredSize.Width < rectangle.Size.Width) return true;
        }

        result = string.Empty;
        measuredSize = SizeF.Empty;
        return false;
    }

    private static bool TryGetFittingFont(
        string text,
        FontFamily fontFamily,
        Rectangle rectangle,
        int steps,
        out Font? font,
        out SizeF measuredSize
    )
    {
        var fontSize = (float)rectangle.Height;
        var decPerStep = fontSize / steps;

        while (fontSize > 0)
        {
            font = new Font(fontFamily, fontSize);
            measuredSize = MeasureString(text, font);
            if (measuredSize.Width < rectangle.Width) return true;
            fontSize -= decPerStep;
        }

        font = null;
        measuredSize = SizeF.Empty;
        return false;
    }

    #endregion

    #region RectangleUtils

    public static Rectangle CenteredRectangle(PointF center, SizeF size)
    {
        return Rectangle.Round(new RectangleF(new PointF(center.X - size.Width / 2, center.Y - size.Height / 2), size));
    }

    public static Rectangle CenteredRectangle(Point center, Size size)
    {
        return new Rectangle(new Point(center.X - size.Width / 2, center.Y - size.Height / 2), size);
    }

    #endregion
}