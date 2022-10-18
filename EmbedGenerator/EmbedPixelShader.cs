internal class EmbedPixelShader {
    #region Constructor / Dispose

    private readonly FloatColor _backgroundColor;
    private readonly FloatColor _coverTintColor;
    private readonly FloatColor _diffColor;
    private readonly LinearGradientTexture _gradientTexture;
    private readonly RoundedRectangleTexture _cornerMaskTexture;
    private readonly ImageTexture _borderMaskTexture;
    private readonly ImageTexture _sharpGradientMaskTexture;
    private readonly ImageTexture _blurredGradientMaskTexture;
    private readonly ImageTexture _coverTexture;
    private readonly ImageTexture _avatarMaskTexture;
    private readonly ImageTexture _avatarShadowTexture;
    private readonly ImageTexture _avatarImageTexture;
    private readonly ImageTexture? _avatarBorderTexture;
    private readonly ImageTexture? _starTexture;
    private readonly ImageTexture _finalMaskTexture;
    private readonly float _hueShiftRadians;
    private readonly float _saturation;

    private readonly bool _hasAvatarBorder;
    private readonly bool _hasStar;

    public EmbedPixelShader(
        FloatColor backgroundColor,
        FloatColor coverTintColor,
        FloatColor diffColor,
        LinearGradientTexture gradientTexture,
        RoundedRectangleTexture cornerMaskTexture,
        ImageTexture borderMaskTexture,
        ImageTexture sharpGradientMaskTexture,
        ImageTexture blurredGradientMaskTexture,
        ImageTexture coverTexture,
        ImageTexture avatarMaskTexture,
        ImageTexture avatarShadowTexture,
        ImageTexture avatarImageTexture,
        ImageTexture? avatarBorderTexture,
        ImageTexture? starTexture,
        ImageTexture finalMaskTexture,
        float hueShiftRadians,
        float saturation
    )
    {
        _backgroundColor = backgroundColor;
        _coverTintColor = coverTintColor;
        _diffColor = diffColor;
        _gradientTexture = gradientTexture;
        _cornerMaskTexture = cornerMaskTexture;
        _borderMaskTexture = borderMaskTexture;
        _sharpGradientMaskTexture = sharpGradientMaskTexture;
        _blurredGradientMaskTexture = blurredGradientMaskTexture;
        _coverTexture = coverTexture;
        _avatarMaskTexture = avatarMaskTexture;
        _avatarShadowTexture = avatarShadowTexture;
        _avatarImageTexture = avatarImageTexture;
        _avatarBorderTexture = avatarBorderTexture;
        _finalMaskTexture = finalMaskTexture;
        _starTexture = starTexture;
        _hueShiftRadians = hueShiftRadians;
        _saturation = saturation;

        _hasAvatarBorder = _avatarBorderTexture != null;
        _hasStar = _starTexture != null;
    }

    #endregion

    #region GetPixel

    public FloatColor GetPixel(int x, int y)
    {
        var gradientColor = _gradientTexture.GetPixel(x, y);

        var borderMask = _borderMaskTexture.GetPixel(x, y);
        borderMask.AlphaBlend(_cornerMaskTexture.GetPixel(x, y));

        var maskedGradient = gradientColor;
        maskedGradient.AlphaMask(_sharpGradientMaskTexture.GetPixel(x, y));

        var border = _backgroundColor;
        border.AlphaBlend(maskedGradient);
        border.AlphaMask(borderMask);

        var avatar = _avatarImageTexture.GetPixel(x, y);
        avatar.AlphaMask(_avatarMaskTexture.GetPixel(x, y));

        maskedGradient = gradientColor;
        maskedGradient.AlphaMask(_blurredGradientMaskTexture.GetPixel(x, y));

        var result = FloatColor.Black;
        result.AlphaBlend(_coverTexture.GetPixel(x, y));
        result.Multiply(_coverTintColor);
        result.AlphaBlend(maskedGradient);
        result.AlphaBlend(border);

        if (_hasAvatarBorder)
        {
            result.AlphaBlend(avatar);
            var avatarOverlay = _avatarBorderTexture!.GetPixel(x, y);
            avatarOverlay.ApplyHsbTransform(_hueShiftRadians, _saturation, 0.0f);
            result.Add(avatarOverlay);
        }
        else
        {
            result.AlphaBlend(_avatarShadowTexture.GetPixel(x, y));
            result.AlphaBlend(avatar);
        }

        if (_hasStar)
        {
            var star = _diffColor;
            star.AlphaMask(_starTexture!.GetPixel(x, y));
            result.AlphaBlend(star);
        }

        result.AlphaMask(_finalMaskTexture.GetPixel(x, y));
        return result;
    }

    #endregion
}