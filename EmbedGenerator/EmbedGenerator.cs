using System;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using ImageProcessor;
using ImageProcessor.Imaging;

internal class EmbedGenerator
{
    #region Constants

    private static readonly FloatColor BackgroundColor = FloatColor.White;
    private static readonly FloatColor CoverTintColor = new(0.6f, 0.6f, 0.6f, 1.0f);
    private const int CoverBlurRadius = 4;

    #endregion

    #region Constructor

    private readonly Image _starImage;
    private readonly ImageTexture _avatarMaskTexture;
    private readonly ImageTexture _avatarShadowTexture;
    private readonly ImageTexture _sharpGradientMaskTexture;
    private readonly ImageTexture _blurredGradientMaskTexture;
    private readonly ImageTexture _borderMaskTexture;
    private readonly ImageTexture _finalMaskTexture;
    private readonly EmbedLayout _layout;
    private readonly FontFamily _fontFamily;
    private readonly Font _diffFont;

    public EmbedGenerator(
        Size size,
        Image starImage,
        Image avatarMask,
        Image avatarShadow,
        Image sharpGradientMask,
        Image blurredGradientMask,
        Image coverMask,
        Image finalMask,
        FontFamily fontFamily
    )
    {
        _fontFamily = fontFamily;
        _layout = new EmbedLayout(size);
        _diffFont = new Font(_fontFamily, _layout.DiffFontSize);

        _starImage = starImage;
        _avatarMaskTexture = new ImageTexture(avatarMask, _layout.AvatarRectangle);
        _avatarShadowTexture = new ImageTexture(avatarShadow, _layout.AvatarOverlayRectangle);
        _sharpGradientMaskTexture = new ImageTexture(sharpGradientMask, _layout.FullRectangle);
        _blurredGradientMaskTexture = new ImageTexture(blurredGradientMask, _layout.FullRectangle);
        _borderMaskTexture = new ImageTexture(coverMask, _layout.FullRectangle);
        _finalMaskTexture = new ImageTexture(finalMask, _layout.FullRectangle);
    }

    #endregion

    #region Generate

    public Image Generate(
        string playerName,
        string songName,
        string modifiers,
        string difficulty,
        float accuracy,
        int rank,
        float pp,
        float stars,
        Image coverImage,
        Image avatarImage,
        Image? avatarBorderImage,
        int overlayHueShift,
        float overlaySaturation,
        Color leftColor,
        Color rightColor,
        Color diffColor
    )
    {
        var hasStars = stars != 0;
        var accuracyText = $"{MathF.Round(accuracy * 100, 2)}%";
        var rankText = pp != 0 ? $"#{rank} • {MathF.Round(pp, 2)}pp" : $"#{rank}";
        var diffText = hasStars ? $"{difficulty} {MathF.Round(stars, 2)}" : difficulty;

        _layout.CalculateCornerRectangles(
            Graphics.FromImage(coverImage), _diffFont, diffText, hasStars,
            out var textRectangle, out var starRectangle, out var cornerAreaRectangle
        );

        var preBlurredCover = PreBlurCover(coverImage);
        var cornerMaskTexture = new RoundedRectangleTexture(FloatColor.White, cornerAreaRectangle, _layout.DiffCornerRadius);
        var coverTexture = new ImageTexture(preBlurredCover, _layout.FullRectangle);
        var avatarImageTexture = new ImageTexture(avatarImage, _layout.AvatarRectangle);
        var avatarBorderTexture = avatarBorderImage == null ? null : new ImageTexture(avatarBorderImage, _layout.AvatarOverlayRectangle);
        var starTexture = hasStars ? new ImageTexture(_starImage, starRectangle) : null;

        var gradientTexture = new LinearGradientTexture(
            FloatColor.FromColor(leftColor), FloatColor.FromColor(rightColor),
            new Vector2(_layout.Width * 0.4f, _layout.Height * 0.6f),
            new Vector2(_layout.Width * 0.6f, _layout.Height * 0.4f)
        );

        var hueShiftRadians = overlayHueShift * MathF.PI / 180;

        var pixelShader = new EmbedPixelShader(
            BackgroundColor,
            CoverTintColor,
            FloatColor.FromColor(diffColor),
            gradientTexture,
            cornerMaskTexture,
            _borderMaskTexture,
            _sharpGradientMaskTexture,
            _blurredGradientMaskTexture,
            coverTexture,
            _avatarMaskTexture,
            _avatarShadowTexture,
            avatarImageTexture,
            avatarBorderTexture,
            starTexture,
            _finalMaskTexture,
            hueShiftRadians,
            overlaySaturation
        );

        var bitmap = new Bitmap(_layout.Width, _layout.Height);
        using (var destination = new FastBitmap(bitmap))
        {
            Parallel.For(0, _layout.Height, (int y) => {
                for (var x = 0; x < _layout.Width; x++)
                {
                    var pixel = pixelShader.GetPixel(x, y).ToColor();
                    destination.SetPixel(x, y, pixel);
                }
            });
        };

        var graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
        graphics.FitText(playerName, Color.White, _fontFamily, _layout.PlayerNameRectangle, _layout.MinPlayerNameFontSize);
        graphics.FitText(songName, Color.White, _fontFamily, _layout.SongNameRectangle, _layout.MinSongNameFontSize);
        graphics.FitText(accuracyText, Color.White, _fontFamily, _layout.AccTextRectangle);
        graphics.FitText(rankText, Color.White, _fontFamily, _layout.RankTextRectangle);
        graphics.FitText(modifiers, Color.White, _fontFamily, _layout.ModifiersTextRectangle);
        graphics.DrawTextCentered(diffText, _diffFont, diffColor, textRectangle);

        return bitmap;
    }

    #endregion

    #region PreBlurCover

    private Image PreBlurCover(Image coverImage)
    {
        var factory = new ImageFactory()
            .Load(coverImage)
            .GaussianBlur(CoverBlurRadius)
            .Resize(new ResizeLayer(_layout.Size, ResizeMode.Crop));

        return factory.Image;
    }

    #endregion
}