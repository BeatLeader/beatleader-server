using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using ImageProcessor;
using ImageProcessor.Imaging;
using ImageProcessor.Imaging.Filters.Photo;
using Lib.AspNetCore.ServerTiming;

internal class EmbedGenerator
{
    #region Constants

    private static readonly Color CoverImageTint = Color.FromArgb(255, 160, 160, 160);

    private readonly NumberFormatInfo _numberFormatInfo = new()
    {
        NumberGroupSeparator = "",
        NumberDecimalSeparator = ".",
        NumberDecimalDigits = 2
    };

    #endregion

    #region Constructor

    private readonly Image _starImage;
    private readonly Image _avatarMask;
    private readonly Image _avatarShadow;
    private readonly Image _gradientMask;
    private readonly Image _gradientMaskBlurred;
    private readonly Image _backgroundImage;
    private readonly Image _coverMask;
    private readonly Image _finalMask;
    private readonly EmbedLayout _layout;
    private readonly Bitmap _fullSizeEmptyBitmap;
    private readonly Bitmap _whitePixelBitmap;
    private readonly FontFamily _fontFamily;
    private readonly Font _diffFont;

    public EmbedGenerator(
        Size size,
        Image starImage,
        Image avatarMask,
        Image avatarShadow,
        Image backgroundImage,
        Image gradientMask,
        Image gradientMaskBlurred,
        Image coverMask,
        Image finalMask,
        FontFamily fontFamily
    )
    {
        _fontFamily = fontFamily;
        _layout = new EmbedLayout(size);
        _diffFont = new Font(_fontFamily, _layout.DiffFontSize);

        _starImage = starImage;
        _avatarMask = avatarMask.ResizeIfNecessary(_layout.AvatarRectangle.Size);
        _avatarShadow = avatarShadow.ResizeIfNecessary(_layout.AvatarOverlayRectangle.Size);
        _backgroundImage = backgroundImage.ResizeIfNecessary(_layout.FullRectangle.Size);
        _gradientMask = gradientMask.ResizeIfNecessary(_layout.FullRectangle.Size);
        _gradientMaskBlurred = gradientMaskBlurred.ResizeIfNecessary(_layout.FullRectangle.Size);
        _coverMask = coverMask.ResizeIfNecessary(_layout.FullRectangle.Size);
        _finalMask = finalMask.ResizeIfNecessary(_layout.FullRectangle.Size);

        _fullSizeEmptyBitmap = new Bitmap(_layout.Width, _layout.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        _whitePixelBitmap = new Bitmap(1, 1);
        _whitePixelBitmap.SetPixel(0, 0, Color.White);
    }

    #endregion

    #region Generate

    public Image Generate(
        IServerTiming serverTiming,
        string playerName,
        string songName,
        string modifiers,
        string difficulty,
        float? accuracy,
        int? rank,
        float? pp,
        float? stars,
        Image coverImage,
        Image avatarImage,
        Image? avatarOverlayImage,
        float? overlayHueShift,
        float? overlaySaturation,
        Color leftColor,
        Color rightColor,
        Color diffColor
    )
    {
        var hasStars = stars != null;
        var accuracyText = accuracy != null ? $"{MathF.Round((float)accuracy * 100, 2)}%" : "";
        var rankText = rank != null ? (pp != null && pp != 0 ? $"#{(int)rank} • {MathF.Round((float)pp, 2)}pp" : $"#{rank}") : "";
        var diffText = hasStars ? $"{difficulty} {((float)stars).ToString(_numberFormatInfo)}" : difficulty;

        var factory = new ImageFactory().Load(_fullSizeEmptyBitmap);
        var graphics = Graphics.FromImage(factory.Image);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        _layout.CalculateCornerRectangles(graphics, _diffFont, diffText, hasStars,
            out var textRectangle,
            out var starRectangle,
            out var cornerAreaRectangle
        );

        var borderGradient = GenerateGradient(_gradientMask, leftColor, rightColor);
        var coverGradient = GenerateGradient(_gradientMaskBlurred, leftColor, rightColor);
        var cover = GenerateCover(coverImage, coverGradient);
        var border = GenerateBorder(cornerAreaRectangle, borderGradient);
        var avatar = GenerateAvatar(avatarImage);

        factory.OverlayRegion(cover).OverlayRegion(border);

        if (avatarOverlayImage == null)
        {
            factory.OverlayRegion(_avatarShadow, _layout.AvatarOverlayRectangle);
        }

        factory.OverlayRegion(avatar, _layout.AvatarRectangle);

        if (avatarOverlayImage != null)
        {
            var avatarOverlay = GenerateAvatarOverlay(avatarOverlayImage, overlayHueShift != null ? (int)overlayHueShift : 0, overlaySaturation ?? 0);
            factory.OverlayRegion(avatarOverlay, _layout.AvatarOverlayRectangle);
        }

        if (hasStars)
        {
            var star = GenerateStar(starRectangle.Size, diffColor);
            factory.OverlayRegion(star, starRectangle);
        }

        graphics.FitText(playerName, Color.White, _fontFamily, _layout.PlayerNameRectangle, _layout.MinPlayerNameFontSize);
        graphics.FitText(songName, Color.White, _fontFamily, _layout.SongNameRectangle, _layout.MinSongNameFontSize);
        graphics.FitText(accuracyText, Color.White, _fontFamily, _layout.AccTextRectangle);
        graphics.FitText(rankText, Color.White, _fontFamily, _layout.RankTextRectangle);
        graphics.FitText(modifiers, Color.White, _fontFamily, _layout.ModifiersTextRectangle);
        graphics.DrawTextCentered(diffText, _diffFont, diffColor, textRectangle);
        // DrawDebugInfo(graphics);

        factory.MaskRegion(_finalMask);
        return factory.Image;
    }

    #endregion

    #region Debug

    private void DrawDebugInfo(Graphics graphics)
    {
        var debugPen = new Pen(new SolidBrush(Color.Red), 1f);
        graphics.DrawRectangle(debugPen, _layout.AvatarRectangle);
        graphics.DrawRectangle(debugPen, _layout.SongNameRectangle);
        graphics.DrawRectangle(debugPen, _layout.PlayerNameRectangle);
        graphics.DrawRectangle(debugPen, _layout.AccTextRectangle);
        graphics.DrawRectangle(debugPen, _layout.RankTextRectangle);
        graphics.DrawRectangle(debugPen, _layout.ModifiersTextRectangle);
    }

    #endregion

    #region GenerateStar

    private Image GenerateStar(Size size, Color color)
    {
        var factory = new ImageFactory()
            .Load(_starImage)
            .Resize(new ResizeLayer(size))
            .Tint(color);

        return factory.Image;
    }

    #endregion

    #region GenerateAvatar

    private Image GenerateAvatar(Image avatarImage)
    {
        var factory = new ImageFactory()
            .Load(_whitePixelBitmap).Filter(MatrixFilters.Invert)
            .Resize(new ResizeLayer(_layout.AvatarRectangle.Size, ResizeMode.Stretch))
            .OverlayRegion(avatarImage)
            .MaskRegion(_avatarMask, _layout.AvatarRectangle.Size);

        return factory.Image;
    }

    #endregion

    #region GenerateAvatarOverlay

    private Image GenerateAvatarOverlay(
        Image avatarOverlayImage,
        int hueShiftDegrees,
        float saturation
    )
    {
        return avatarOverlayImage
            .ApplyHsbTransform(hueShiftDegrees, saturation, 0f)
            .ResizeIfNecessary(_layout.AvatarOverlayRectangle.Size);
    }

    #endregion

    #region GenerateGradients

    private Image GenerateGradient(Image mask, Color leftColor, Color rightColor)
    {
        var gradientBrush = new LinearGradientBrush(
            new Point(0, _layout.Height),
            new Point(_layout.Width, 0),
            leftColor, rightColor
        );

        var factory = new ImageFactory().Load(_fullSizeEmptyBitmap);
        var graphics = Graphics.FromImage(factory.Image);
        graphics.FillRectangle(gradientBrush, _layout.FullRectangle);
        factory.MaskRegion(mask);

        return factory.Image;
    }

    #endregion

    #region GenerateBorder

    private Image GenerateBorder(Rectangle cornerRectangle, Image gradient)
    {
        var cornerFactory = new ImageFactory()
            .Load(_whitePixelBitmap)
            .Resize(new ResizeLayer(cornerRectangle.Size, ResizeMode.Stretch))
            .RoundedCorners(_layout.DiffCornerRadius);

        var maskFactory = new ImageFactory()
            .Load(_coverMask)
            .OverlayRegion(cornerFactory.Image, cornerRectangle);

        var factory = new ImageFactory()
            .Load(_backgroundImage)
            .OverlayRegion(gradient)
            .MaskRegion(maskFactory.Image);

        return factory.Image;
    }

    #endregion

    #region GenerateCover

    private Image GenerateCover(Image coverImage, Image gradient)
    {
        var factory = new ImageFactory()
            .Load(coverImage)
            .Resize(new ResizeLayer(_layout.Size, ResizeMode.Crop))
            .Tint(CoverImageTint)
            .OverlayRegion(gradient);

        return factory.Image;
    }

    #endregion
}