using BeatLeader_Server.Models;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;

internal class EmbedGenerator
{
    #region Constants

    private static readonly FloatColor BackgroundColor = FloatColor.White;
    private static readonly FloatColor CoverTintColor = new(0.6f, 0.6f, 0.6f, 1.0f);
    private const int CoverBlurRadius = 1;

    #endregion

    #region Constructor

    private readonly Image<Rgba32> _starImage;
    private readonly ImageTexture _avatarMaskTexture;
    private readonly ImageTexture _avatarShadowTexture;
    private readonly ImageTexture _sharpGradientMaskTexture;
    private readonly ImageTexture _blurredGradientMaskTexture;
    private readonly ImageTexture _borderMaskTexture;
    private readonly ImageTexture _finalMaskTexture;
    private readonly EmbedLayout _layout;
    private readonly FontFamily _fontFamily;
    private readonly Font _diffFont;
    private readonly IReadOnlyList<FontFamily> _fallbackFamilies;

    public EmbedGenerator(
        Size size,
        Image<Rgba32> starImage,
        Image<Rgba32> avatarMask,
        Image<Rgba32> avatarShadow,
        Image<Rgba32> sharpGradientMask,
        Image<Rgba32> blurredGradientMask,
        Image<Rgba32> coverMask,
        Image<Rgba32> finalMask,
        FontFamily fontFamily,
        IReadOnlyList<FontFamily> fallbackFamilies
    )
    {
        _fontFamily = fontFamily;
        _layout = new EmbedLayout(size);
        _diffFont = new Font(_fontFamily, _layout.DiffFontSize);
        _fallbackFamilies = fallbackFamilies;

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

    public Image<Rgba32> Generate(
        string playerName,
        string songName,
        string modifiers,
        string difficulty,
        float accuracy,
        int rank,
        float pp,
        float stars,
        LeaderboardContexts context,
        Image<Rgba32> coverImage,
        Image<Rgba32> avatarImage,
        Image<Rgba32>? avatarBorderImage,
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

        _layout.CalculateCornerRectangles(_diffFont, diffText, hasStars,
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

        var bitmap = new Image<Rgba32>(_layout.Width, _layout.Height);
        Parallel.For(0, _layout.Height, (int y) => {
            for (var x = 0; x < _layout.Width; x++)
            {
                bitmap[x, y] = pixelShader.GetPixel(x, y).ToColor();
            }
        });

        bitmap.FitText(playerName, Color.White, _fontFamily, _fallbackFamilies, _layout.PlayerNameRectangle, _layout.MinPlayerNameFontSize);
        bitmap.FitText(songName, Color.White, _fontFamily, _fallbackFamilies, _layout.SongNameRectangle, _layout.MinSongNameFontSize);
        bitmap.FitText(accuracyText, Color.White, _fontFamily, _fallbackFamilies, _layout.AccTextRectangle);
        bitmap.FitText(rankText, Color.White, _fontFamily, _fallbackFamilies, _layout.RankTextRectangle);
        bitmap.FitText(modifiers, Color.White, _fontFamily, _fallbackFamilies, _layout.ModifiersTextRectangle);
        if (context != LeaderboardContexts.General && context != LeaderboardContexts.None) {
            bitmap.FitText(context.ToString(), Color.White, _fontFamily, _fallbackFamilies, _layout.ContextTextRectangle);
        }
        bitmap.DrawTextCentered(diffText, _diffFont, _fallbackFamilies, diffColor, textRectangle);

        return bitmap;
    }

    #endregion

    #region PreBlurCover

    private Image<Rgba32> PreBlurCover(Image<Rgba32> coverImage)
    {
        coverImage.Mutate(x => x
        .GaussianBlur(CoverBlurRadius)
        .Resize(new ResizeOptions { Size = _layout.Size } ));

        return coverImage;
    }

    #endregion
}