using SixLabors.Fonts;
using SixLabors.ImageSharp;

internal class EmbedLayout {
    #region Properties

    public readonly Rectangle FullRectangle;
    public readonly Rectangle AvatarRectangle;
    public readonly Rectangle AvatarOverlayRectangle;
    public readonly Rectangle SongNameRectangle;
    public readonly Rectangle PlayerNameRectangle;
    public readonly Rectangle AccTextRectangle;
    public readonly Rectangle RankTextRectangle;
    public readonly Rectangle ModifiersTextRectangle;

    public readonly float MinPlayerNameFontSize;
    public readonly float MinSongNameFontSize;
    public readonly float DiffFontSize;
    public readonly int DiffCornerRadius;

    public readonly Size Size;
    public readonly int Width;
    public readonly int Height;

    #endregion

    #region Constructor

    public EmbedLayout(Size size)
    {
        FullRectangle = new Rectangle(Point.Empty, size);
        Width = size.Width;
        Height = size.Height;
        Size = size;

        MinPlayerNameFontSize = size.Height * 0.07f;
        MinSongNameFontSize = size.Height * 0.1f;
        DiffFontSize = size.Height * 0.06f;
        DiffCornerRadius = (int)(size.Height * 0.03f);

        var center = new PointF(size.Width * 0.5f, size.Height * 0.5f);

        var avatarOrigin = center with { X = center.X - size.Width * 0.22f };
        var avatarSize = new SizeF(size.Height * 0.5f, size.Height * 0.5f);
        var avatarOverlaySize = new SizeF(avatarSize.Width * 1.5f, avatarSize.Height * 1.5f);
        AvatarRectangle = DrawingUtils.CenteredRectangle(avatarOrigin, avatarSize);
        AvatarOverlayRectangle = DrawingUtils.CenteredRectangle(avatarOrigin, avatarOverlaySize);

        var songNameOrigin = center with { Y = center.Y + size.Height * 0.35f };
        var songNameSize = new SizeF(size.Width * 0.94f, size.Height * 0.12f);
        SongNameRectangle = DrawingUtils.CenteredRectangle(songNameOrigin, songNameSize);

        var playerNameOrigin = avatarOrigin with { Y = avatarOrigin.Y - size.Height * 0.35f };
        var playerNameSize = new SizeF(size.Width * 0.5f, size.Height * 0.12f);
        PlayerNameRectangle = DrawingUtils.CenteredRectangle(playerNameOrigin, playerNameSize);

        var statsOrigin = center with { X = center.X + size.Width * 0.22f };

        var accTextOrigin = statsOrigin with { Y = statsOrigin.Y - size.Height * 0.164f };
        var accTextSize = new SizeF(size.Width * 0.5f, size.Height * 0.146f);
        AccTextRectangle = DrawingUtils.CenteredRectangle(accTextOrigin, accTextSize);

        var rankTextOrigin = statsOrigin with { Y = statsOrigin.Y - size.Height * 0.006f };
        var rankTextSize = new SizeF(size.Width * 0.5f, size.Height * 0.11f);
        RankTextRectangle = DrawingUtils.CenteredRectangle(rankTextOrigin, rankTextSize);

        var modifiersTextOrigin = statsOrigin with { Y = statsOrigin.Y + size.Height * 0.13f };
        var modifiersTextSize = new SizeF(size.Width * 0.5f, size.Height * 0.061f);
        ModifiersTextRectangle = DrawingUtils.CenteredRectangle(modifiersTextOrigin, modifiersTextSize);
    }

    #endregion

    #region CalculateCornerRectangles

    public void CalculateCornerRectangles(Font font, string diffText, bool hasStars,
        out Rectangle textRectangle,
        out Rectangle starRectangle,
        out Rectangle cornerAreaRectangle
    )
    {
        var pad = font.Size * 0.53f;

        var textSize = new SizeF(DrawingUtils.MeasureString(diffText, font).Width, font.Size);
        var starSize = hasStars ? textSize with { Width = textSize.Height } : SizeF.Empty;
        var cornerAreaSize = new SizeF(textSize.Width + starSize.Width + pad * 2, textSize.Height + pad * 2);

        var cornerAreaCenter = new PointF(Width - cornerAreaSize.Width / 2, cornerAreaSize.Height / 2);
        var textCenter = cornerAreaCenter with { X = cornerAreaCenter.X - starSize.Width / 2 };
        var starCenter = textCenter with { X = textCenter.X + (textSize.Width + starSize.Width) / 2 };

        cornerAreaRectangle = DrawingUtils.CenteredRectangle(cornerAreaCenter, cornerAreaSize);
        textRectangle = DrawingUtils.CenteredRectangle(textCenter, textSize);
        starRectangle = DrawingUtils.CenteredRectangle(starCenter, starSize);
    }

    #endregion
}