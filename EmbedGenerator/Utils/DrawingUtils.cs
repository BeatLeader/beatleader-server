using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

internal static class DrawingUtils {
    #region Resized

    public static Image Resized(this Image source, Size newSize)
    {
        if (newSize == source.Size) return source;

        var destRect = new RectangleF(PointF.Empty, newSize);
        var srcRect = new RectangleF(PointF.Empty, source.Size);

        var bitmap = new Bitmap(newSize.Width, newSize.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(source, destRect, srcRect, GraphicsUnit.Pixel);
        return bitmap;
    }

    #endregion

    #region DrawTextCentered

    public static void DrawTextCentered(
        this Graphics graphics,
        string text,
        Font font,
        Color color,
        Rectangle rectangle
    )
    {
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
    )
    {
        var canFit = graphics.TryGetFittingFont(text, fontFamily, rectangle, 40, out var fittingFont, out var fittingSize);
        if (!canFit || fittingFont!.Size < minimalFontSize)
        {
            fittingFont = new Font(fontFamily, minimalFontSize);
            graphics.TryGetFittingString(text, fittingFont, rectangle, out text, out fittingSize);
            if (graphics.TryGetFittingFont(text, fontFamily, rectangle, 40, out var finalFont, out var finalSize))
            {
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
            measuredSize = graphics.MeasureString(result, font);
            if (measuredSize.Width < rectangle.Size.Width) return true;
        }

        var chars = text.ToCharArray();
        for (var i = chars.Length - 1; i > 0; i--)
        {
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
    )
    {
        var fontSize = (float)rectangle.Height;
        var decPerStep = fontSize / steps;

        while (fontSize > 0)
        {
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